using System.Data;
using ERP.Application.Sales;
using ERP.Domain.Catalog;
using ERP.Domain.Common;
using ERP.Domain.Customers;
using ERP.Domain.Inventory;
using ERP.Domain.Sales;
using ERP.Persistence.Database;
using FirebirdSql.Data.FirebirdClient;

namespace ERP.Persistence.Sales;

public sealed class FirebirdSaleRepository : ISaleRepository
{
    private readonly FirebirdUnitOfWork _unitOfWork;

    public FirebirdSaleRepository(FirebirdUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Sale?> GetAsync(SaleId saleId, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(
            """
            SELECT WAREHOUSE_ID, CUSTOMER_ID, STATUS, DISCOUNT_RIALS,
                   PAYMENT_METHOD, OPENED_AT_UTC, COMPLETED_AT_UTC, SERVICE_CHARGE_RIALS, NUMBER,
                   TAX_RIALS
            FROM SALE
            WHERE ID = @ID
            """);
        command.Parameters.Add("@ID", FbDbType.Char).Value = saleId.ToString();

        WarehouseId warehouseId;
        CustomerId? customerId;
        Money? tax;
        SaleStatus status;
        SaleNumber? number;
        long discountRials;
        long serviceChargeRials;
        PaymentMethod? paymentMethod;
        DateTimeOffset openedAtUtc;
        DateTimeOffset? completedAtUtc;

        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            warehouseId = WarehouseId.From(Guid.Parse(reader.GetString(0)));
            customerId = reader.IsDBNull(1) ? null : CustomerId.From(Guid.Parse(reader.GetString(1)));
            status = (SaleStatus)reader.GetInt16(2);
            discountRials = reader.GetInt64(3);
            paymentMethod = reader.IsDBNull(4) ? null : (PaymentMethod)reader.GetInt16(4);
            openedAtUtc = new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(5), DateTimeKind.Utc));
            completedAtUtc = reader.IsDBNull(6)
                ? null
                : new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(6), DateTimeKind.Utc));
            serviceChargeRials = reader.GetInt64(7);
            number = reader.IsDBNull(8) ? null : SaleNumber.From(reader.GetInt64(8));
            tax = reader.IsDBNull(9) ? null : Money.FromRials(reader.GetInt64(9));
        }

        var lines = await ReadLinesAsync(saleId, cancellationToken).ConfigureAwait(false);

        return Sale.Rehydrate(
            saleId,
            warehouseId,
            customerId,
            openedAtUtc,
            status,
            number,
            Money.FromRials(discountRials),
            Money.FromRials(serviceChargeRials),
            paymentMethod,
            completedAtUtc,
            tax,
            lines);
    }

    public async Task SaveAsync(Sale sale, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sale);

        await using (var command = CreateCommand(
            """
            UPDATE OR INSERT INTO SALE (
                ID, WAREHOUSE_ID, CUSTOMER_ID, STATUS, DISCOUNT_RIALS,
                PAYMENT_METHOD, OPENED_AT_UTC, COMPLETED_AT_UTC, SERVICE_CHARGE_RIALS, NUMBER,
                TAX_RIALS, TOTAL_RIALS)
            VALUES (
                @ID, @WAREHOUSE_ID, @CUSTOMER_ID, @STATUS, @DISCOUNT_RIALS,
                @PAYMENT_METHOD, @OPENED_AT_UTC, @COMPLETED_AT_UTC, @SERVICE_CHARGE_RIALS, @NUMBER,
                @TAX_RIALS, @TOTAL_RIALS)
            MATCHING (ID)
            """))
        {
            command.Parameters.Add("@ID", FbDbType.Char).Value = sale.Id.ToString();
            command.Parameters.Add("@WAREHOUSE_ID", FbDbType.Char).Value = sale.WarehouseId.ToString();
            command.Parameters.Add("@CUSTOMER_ID", FbDbType.Char).Value =
                sale.CustomerId is { } customerId ? customerId.ToString() : DBNull.Value;
            command.Parameters.Add("@STATUS", FbDbType.SmallInt).Value = (short)sale.Status;
            command.Parameters.Add("@NUMBER", FbDbType.BigInt).Value =
                sale.Number is { } number ? number.Value : DBNull.Value;
            command.Parameters.Add("@DISCOUNT_RIALS", FbDbType.BigInt).Value = sale.Discount.Rials;
            command.Parameters.Add("@SERVICE_CHARGE_RIALS", FbDbType.BigInt).Value = sale.ServiceCharge.Rials;
            command.Parameters.Add("@PAYMENT_METHOD", FbDbType.SmallInt).Value =
                sale.PaymentMethod is { } method ? (short)method : DBNull.Value;
            command.Parameters.Add("@OPENED_AT_UTC", FbDbType.TimeStamp).Value = sale.OpenedAtUtc.UtcDateTime;
            command.Parameters.Add("@COMPLETED_AT_UTC", FbDbType.TimeStamp).Value =
                sale.CompletedAtUtc is { } completedAt ? completedAt.UtcDateTime : DBNull.Value;
            command.Parameters.Add("@TAX_RIALS", FbDbType.BigInt).Value =
                sale.Totals is { } taxTotals ? taxTotals.Tax.Rials : DBNull.Value;
            command.Parameters.Add("@TOTAL_RIALS", FbDbType.BigInt).Value =
                sale.Totals is { } totals ? totals.Total.Rials : DBNull.Value;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        // The cart is small and lines can be removed as well as added, so a
        // delete-then-reinsert of the whole line set is simpler and just as
        // correct as diffing — unlike INVENTORY_LAYER/STOCK_MOVEMENT, which are
        // append-only and diffing does not apply to.
        await using (var deleteCommand = CreateCommand("DELETE FROM SALE_LINE WHERE SALE_ID = @SALE_ID"))
        {
            deleteCommand.Parameters.Add("@SALE_ID", FbDbType.Char).Value = sale.Id.ToString();
            await deleteCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var line in sale.Lines)
        {
            await using var lineCommand = CreateCommand(
                """
                INSERT INTO SALE_LINE (
                    SALE_ID, PRODUCT_ID, QUANTITY, UNIT_PRICE_RIALS,
                    DISCOUNT_RIALS, CATALOG_PRICE_RIALS)
                VALUES (
                    @SALE_ID, @PRODUCT_ID, @QUANTITY, @UNIT_PRICE_RIALS,
                    @DISCOUNT_RIALS, @CATALOG_PRICE_RIALS)
                """);
            lineCommand.Parameters.Add("@SALE_ID", FbDbType.Char).Value = sale.Id.ToString();
            lineCommand.Parameters.Add("@PRODUCT_ID", FbDbType.Char).Value = line.ProductId.ToString();
            lineCommand.Parameters.Add("@QUANTITY", FbDbType.Decimal).Value = line.Quantity.Value;
            lineCommand.Parameters.Add("@UNIT_PRICE_RIALS", FbDbType.BigInt).Value = line.UnitPrice.Rials;
            lineCommand.Parameters.Add("@DISCOUNT_RIALS", FbDbType.BigInt).Value = line.Discount.Rials;
            lineCommand.Parameters.Add("@CATALOG_PRICE_RIALS", FbDbType.BigInt).Value = line.CatalogPrice.Rials;
            await lineCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<List<(ProductId ProductId, decimal Quantity, long UnitPriceRials, long DiscountRials, long CatalogPriceRials)>> ReadLinesAsync(
        SaleId saleId,
        CancellationToken cancellationToken)
    {
        var lines = new List<(ProductId, decimal, long, long, long)>();
        await using var command = CreateCommand(
            """
            SELECT PRODUCT_ID, QUANTITY, UNIT_PRICE_RIALS, DISCOUNT_RIALS, CATALOG_PRICE_RIALS
            FROM SALE_LINE
            WHERE SALE_ID = @SALE_ID
            """);
        command.Parameters.Add("@SALE_ID", FbDbType.Char).Value = saleId.ToString();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            lines.Add((
                ProductId.From(Guid.Parse(reader.GetString(0))),
                reader.GetDecimal(1),
                reader.GetInt64(2),
                reader.GetInt64(3),
                reader.GetInt64(4)));
        }

        return lines;
    }

    private FbCommand CreateCommand(string commandText)
    {
        return new FbCommand(commandText, _unitOfWork.Connection, _unitOfWork.Transaction)
        {
            CommandType = CommandType.Text,
        };
    }
}
