using System.Data;
using ERP.Application.Inventory;
using ERP.Domain.Catalog;
using ERP.Domain.Common;
using ERP.Domain.Inventory;
using ERP.Persistence.Database;
using FirebirdSql.Data.FirebirdClient;

namespace ERP.Persistence.Inventory;

/// <summary>
/// Backs «کاردکس کالا»: one product's movements (all warehouses) with the number of the
/// invoice or return each one came from. A movement stores where it came from as text
/// («sale:{id}», «return:{id}»); the number is looked up here, in the same query, so the
/// screen never has to ask again per row.
/// </summary>
public sealed class FirebirdStockCardReader : IStockCardReader
{
    private const string SalePrefix = "sale:";
    private const string ReturnPrefix = "return:";
    private const string OpeningPrefix = "opening:";

    private readonly FirebirdConnectionFactory _connectionFactory;

    public FirebirdStockCardReader(FirebirdConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<StockCardSource?> ReadAsync(ProductId productId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        string name;
        string? sku;
        string unit;
        await using (var header = new FbCommand(
            """
            SELECT P.NAME, P.SKU, U.SYMBOL
            FROM PRODUCT P
            JOIN PRODUCT_UNIT U ON U.ID = P.BASE_UNIT_ID
            WHERE P.ID = @ID
            """,
            connection))
        {
            header.CommandType = CommandType.Text;
            header.Parameters.Add("@ID", FbDbType.Char).Value = productId.ToString();
            await using var reader = await header.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            name = reader.GetString(0);
            sku = reader.IsDBNull(1) ? null : reader.GetString(1);
            unit = reader.GetString(2);
        }

        var movements = new List<StockCardMovement>();
        await using (var command = new FbCommand(
            $"""
            SELECT M.OCCURRED_AT_UTC, M.MOVEMENT_TYPE, M.QUANTITY, M.SOURCE_REFERENCE,
                   CASE
                       WHEN M.SOURCE_REFERENCE STARTING WITH '{SalePrefix}' THEN
                           (SELECT S.NUMBER FROM SALE S WHERE S.ID = SUBSTRING(M.SOURCE_REFERENCE FROM {SalePrefix.Length + 1}))
                       WHEN M.SOURCE_REFERENCE STARTING WITH '{ReturnPrefix}' THEN
                           (SELECT R.NUMBER FROM SALE_RETURN R WHERE R.ID = SUBSTRING(M.SOURCE_REFERENCE FROM {ReturnPrefix.Length + 1}))
                   END,
                   CASE
                       WHEN M.SOURCE_REFERENCE STARTING WITH '{SalePrefix}' THEN
                           (SELECT S.NUMBER FROM SALE S WHERE S.ID = SUBSTRING(M.SOURCE_REFERENCE FROM {SalePrefix.Length + 1}))
                       WHEN M.SOURCE_REFERENCE STARTING WITH '{ReturnPrefix}' THEN
                           (SELECT S2.NUMBER FROM SALE_RETURN R2 JOIN SALE S2 ON S2.ID = R2.SALE_ID
                            WHERE R2.ID = SUBSTRING(M.SOURCE_REFERENCE FROM {ReturnPrefix.Length + 1}))
                   END,
                   -- the FIFO cost of the whole movement, from where each kind of movement recorded it
                   CASE
                       WHEN M.SOURCE_REFERENCE STARTING WITH '{OpeningPrefix}' THEN
                           (SELECT L.UNIT_COST_RIALS * M.QUANTITY FROM INVENTORY_LAYER L
                            WHERE L.ID = SUBSTRING(M.SOURCE_REFERENCE FROM {OpeningPrefix.Length + 1}))
                       WHEN M.SOURCE_REFERENCE STARTING WITH '{ReturnPrefix}' THEN
                           (SELECT RL.UNIT_COST_RIALS * M.QUANTITY FROM SALE_RETURN_LINE RL
                            WHERE RL.RETURN_ID = SUBSTRING(M.SOURCE_REFERENCE FROM {ReturnPrefix.Length + 1})
                              AND RL.PRODUCT_ID = M.PRODUCT_ID)
                       WHEN M.SOURCE_REFERENCE STARTING WITH '{SalePrefix}' AND M.MOVEMENT_TYPE = {(int)StockMovementType.Sale} THEN
                           (SELECT C.COST_RIALS FROM SALE_LINE_COST C
                            WHERE C.SALE_ID = SUBSTRING(M.SOURCE_REFERENCE FROM {SalePrefix.Length + 1})
                              AND C.PRODUCT_ID = M.PRODUCT_ID)
                   END
            FROM STOCK_MOVEMENT M
            WHERE M.PRODUCT_ID = @ID
            ORDER BY M.OCCURRED_AT_UTC, M.ID
            """,
            connection))
        {
            command.CommandType = CommandType.Text;
            command.Parameters.Add("@ID", FbDbType.Char).Value = productId.ToString();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var reference = reader.GetString(3);
                var kind = reference.StartsWith(SalePrefix, StringComparison.Ordinal)
                    ? StockCardDocumentKind.Sale
                    : reference.StartsWith(ReturnPrefix, StringComparison.Ordinal)
                        ? StockCardDocumentKind.Return
                        : StockCardDocumentKind.None;

                movements.Add(new StockCardMovement(
                    new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(0), DateTimeKind.Utc)),
                    (StockMovementType)reader.GetInt16(1),
                    reader.GetDecimal(2),
                    kind,
                    reader.IsDBNull(4) ? null : reader.GetInt64(4),
                    reader.IsDBNull(5) ? null : reader.GetInt64(5),
                    reader.IsDBNull(6) ? null : Money.FromRials((long)Math.Round(reader.GetDecimal(6), MidpointRounding.AwayFromZero))));
            }
        }

        return new StockCardSource(name, sku, unit, movements);
    }
}
