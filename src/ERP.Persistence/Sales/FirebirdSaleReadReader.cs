using System.Data;
using System.Globalization;
using ERP.Application.Sales;
using ERP.Domain.Catalog;
using ERP.Domain.Common;
using ERP.Domain.Customers;
using ERP.Domain.Inventory;
using ERP.Domain.Sales;
using ERP.Persistence.Database;
using FirebirdSql.Data.FirebirdClient;

namespace ERP.Persistence.Sales;

public sealed class FirebirdSaleReadReader : ISaleReadReader
{
    private const string ListColumns =
        """
        S.ID, S.NUMBER, S.OPENED_AT_UTC, S.COMPLETED_AT_UTC, S.CUSTOMER_ID, C.NAME,
        (SELECT COUNT(*) FROM SALE_LINE L WHERE L.SALE_ID = S.ID) AS ITEM_COUNT
        """;

    private readonly FirebirdUnitOfWork _unitOfWork;

    public FirebirdSaleReadReader(FirebirdUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    /// <summary>
    /// Available stock is computed the way <see cref="StockLedger.AvailableQuantity"/>
    /// defines it — remaining quantity of every layer, minus backordered sales —
    /// but summed in the database, because loading a ledger replays its whole
    /// movement history and a cart row only needs the balance.
    /// </summary>
    public async Task<IReadOnlyDictionary<ProductId, SaleProductInfo>> ReadProductsAsync(
        WarehouseId warehouseId,
        IReadOnlyCollection<ProductId> productIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(productIds);

        var result = new Dictionary<ProductId, SaleProductInfo>();
        if (productIds.Count == 0)
        {
            return result;
        }

        var parameterNames = productIds.Select((_, index) => $"@P{index.ToString(CultureInfo.InvariantCulture)}").ToList();
        await using var command = CreateCommand(
            $"""
            SELECT P.ID, P.NAME, P.SKU, U.SYMBOL,
                   COALESCE((SELECT SUM(IL.REMAINING_QTY) FROM INVENTORY_LAYER IL
                             WHERE IL.PRODUCT_ID = P.ID AND IL.WAREHOUSE_ID = @WAREHOUSE_ID), 0)
                 - COALESCE((SELECT SUM(SM.QUANTITY) FROM STOCK_MOVEMENT SM
                             WHERE SM.PRODUCT_ID = P.ID AND SM.WAREHOUSE_ID = @WAREHOUSE_ID
                               AND SM.MOVEMENT_TYPE = @BACKORDER), 0) AS AVAILABLE
            FROM PRODUCT P
            JOIN PRODUCT_UNIT U ON U.ID = P.BASE_UNIT_ID
            WHERE P.ID IN ({string.Join(", ", parameterNames)})
            """);
        command.Parameters.Add("@WAREHOUSE_ID", FbDbType.Char).Value = warehouseId.ToString();
        command.Parameters.Add("@BACKORDER", FbDbType.SmallInt).Value = (short)StockMovementType.BackorderSale;
        foreach (var (productId, name) in productIds.Zip(parameterNames))
        {
            command.Parameters.Add(name, FbDbType.Char).Value = productId.ToString();
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var productId = ProductId.From(Guid.Parse(reader.GetString(0)));
            result[productId] = new SaleProductInfo(
                productId,
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3),
                StockBalance.From(reader.GetDecimal(4)));
        }

        return result;
    }

    public async Task<IReadOnlyList<SaleListItem>> ListCompletedAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(
            $"""
            SELECT {ListColumns}, S.TOTAL_RIALS, S.PAYMENT_METHOD
            FROM SALE S
            LEFT JOIN CUSTOMER C ON C.ID = S.CUSTOMER_ID
            WHERE S.STATUS = @COMPLETED
              AND S.COMPLETED_AT_UTC >= @FROM_UTC AND S.COMPLETED_AT_UTC < @TO_UTC
            ORDER BY S.NUMBER DESC
            """);
        command.Parameters.Add("@COMPLETED", FbDbType.SmallInt).Value = (short)SaleStatus.Completed;
        command.Parameters.Add("@FROM_UTC", FbDbType.TimeStamp).Value = fromUtc.UtcDateTime;
        command.Parameters.Add("@TO_UTC", FbDbType.TimeStamp).Value = toUtc.UtcDateTime;

        return await ReadListAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// A draft's amount is its goods subtotal, rounded per line to whole Rials
    /// exactly as <see cref="SaleLine.GrossAmount"/> rounds (half away from
    /// zero; every quantity and price here is positive, where Firebird's ROUND
    /// behaves the same way).
    /// </summary>
    public async Task<IReadOnlyList<SaleListItem>> ListDraftsWithItemsAsync(CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(
            $"""
            SELECT {ListColumns},
                   (SELECT SUM(CAST(ROUND(L.QUANTITY * L.UNIT_PRICE_RIALS, 0) AS BIGINT) - L.DISCOUNT_RIALS)
                    FROM SALE_LINE L WHERE L.SALE_ID = S.ID) AS SUBTOTAL_RIALS,
                   CAST(NULL AS SMALLINT) AS PAYMENT_METHOD
            FROM SALE S
            LEFT JOIN CUSTOMER C ON C.ID = S.CUSTOMER_ID
            WHERE S.STATUS = @DRAFT
              AND EXISTS (SELECT 1 FROM SALE_LINE L WHERE L.SALE_ID = S.ID)
            ORDER BY S.OPENED_AT_UTC
            """);
        command.Parameters.Add("@DRAFT", FbDbType.SmallInt).Value = (short)SaleStatus.Draft;

        return await ReadListAsync(command, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<SaleListItem>> ReadListAsync(
        FbCommand command,
        CancellationToken cancellationToken)
    {
        var items = new List<SaleListItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            items.Add(new SaleListItem(
                SaleId.From(Guid.Parse(reader.GetString(0))),
                reader.IsDBNull(1) ? null : SaleNumber.From(reader.GetInt64(1)),
                AsUtc(reader.GetDateTime(2)),
                reader.IsDBNull(3) ? null : AsUtc(reader.GetDateTime(3)),
                reader.IsDBNull(4) ? null : CustomerId.From(Guid.Parse(reader.GetString(4))),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                Convert.ToInt32(reader.GetValue(6), CultureInfo.InvariantCulture),
                reader.IsDBNull(7) ? null : Money.FromRials(reader.GetInt64(7)),
                reader.IsDBNull(8) ? null : (PaymentMethod)reader.GetInt16(8)));
        }

        return items;
    }

    private static DateTimeOffset AsUtc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private FbCommand CreateCommand(string commandText)
    {
        return new FbCommand(commandText, _unitOfWork.Connection, _unitOfWork.Transaction)
        {
            CommandType = CommandType.Text,
        };
    }
}
