using System.Data;
using ERP.Application.Common;
using ERP.Application.Sales;
using ERP.Domain.Catalog;
using ERP.Domain.Common;
using ERP.Domain.Sales;
using ERP.Persistence.Database;
using FirebirdSql.Data.FirebirdClient;

namespace ERP.Persistence.Sales;

public sealed class FirebirdSaleReturnRepository : ISaleReturnRepository
{
    private readonly FirebirdUnitOfWork _unitOfWork;

    public FirebirdSaleReturnRepository(FirebirdUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task AddAsync(SaleReturn saleReturn, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(saleReturn);

        await using (var command = CreateCommand(
            """
            INSERT INTO SALE_RETURN (
                ID, SALE_ID, WAREHOUSE_ID, CUSTOMER_ID, NUMBER, REFUND_METHOD, REASON, COMPLETED_AT_UTC,
                SERVICE_NET_RIALS, SERVICE_TAX_RIALS)
            VALUES (
                @ID, @SALE_ID, @WAREHOUSE_ID, @CUSTOMER_ID, @NUMBER, @REFUND_METHOD, @REASON, @COMPLETED_AT_UTC,
                @SERVICE_NET_RIALS, @SERVICE_TAX_RIALS)
            """))
        {
            command.Parameters.Add("@ID", FbDbType.Char).Value = saleReturn.Id.ToString();
            command.Parameters.Add("@SALE_ID", FbDbType.Char).Value = saleReturn.SaleId.ToString();
            command.Parameters.Add("@WAREHOUSE_ID", FbDbType.Char).Value = saleReturn.WarehouseId.ToString();
            command.Parameters.Add("@CUSTOMER_ID", FbDbType.Char).Value =
                saleReturn.CustomerId is { } customerId ? customerId.ToString() : DBNull.Value;
            command.Parameters.Add("@NUMBER", FbDbType.BigInt).Value = saleReturn.Number.Value;
            command.Parameters.Add("@REFUND_METHOD", FbDbType.SmallInt).Value = (short)saleReturn.RefundMethod;
            command.Parameters.Add("@REASON", FbDbType.VarChar).Value = saleReturn.Reason;
            command.Parameters.Add("@COMPLETED_AT_UTC", FbDbType.TimeStamp).Value = saleReturn.CompletedAtUtc.UtcDateTime;
            command.Parameters.Add("@SERVICE_NET_RIALS", FbDbType.BigInt).Value = saleReturn.ServiceCharge.Net.Rials;
            command.Parameters.Add("@SERVICE_TAX_RIALS", FbDbType.BigInt).Value = saleReturn.ServiceCharge.Tax.Rials;
            try
            {
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (FbException exception) when (FirebirdConstraint.IsViolation(exception, "UX_SALE_RETURN_SERVICE_ONCE"))
            {
                throw new DataConflictException(
                    "sales.return.service-charge-already-refunded",
                    "«خدمات/هزینه»ی این فاکتور همین حالا در مرجوعی دیگری پس داده شد.",
                    exception);
            }
        }

        foreach (var line in saleReturn.Lines)
        {
            await using var lineCommand = CreateCommand(
                """
                INSERT INTO SALE_RETURN_LINE (
                    RETURN_ID, PRODUCT_ID, QUANTITY, DISPOSITION, NET_RIALS, TAX_RIALS, UNIT_COST_RIALS)
                VALUES (
                    @RETURN_ID, @PRODUCT_ID, @QUANTITY, @DISPOSITION, @NET_RIALS, @TAX_RIALS, @UNIT_COST_RIALS)
                """);
            lineCommand.Parameters.Add("@RETURN_ID", FbDbType.Char).Value = saleReturn.Id.ToString();
            lineCommand.Parameters.Add("@PRODUCT_ID", FbDbType.Char).Value = line.ProductId.ToString();
            lineCommand.Parameters.Add("@QUANTITY", FbDbType.Decimal).Value = line.Quantity.Value;
            lineCommand.Parameters.Add("@DISPOSITION", FbDbType.SmallInt).Value = (short)line.Disposition;
            lineCommand.Parameters.Add("@NET_RIALS", FbDbType.BigInt).Value = line.Net.Rials;
            lineCommand.Parameters.Add("@TAX_RIALS", FbDbType.BigInt).Value = line.Tax.Rials;
            lineCommand.Parameters.Add("@UNIT_COST_RIALS", FbDbType.BigInt).Value =
                line.UnitCost is { } unitCost ? unitCost.Rials : DBNull.Value;
            await lineCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyDictionary<ProductId, PreviouslyReturned>> GetReturnedAsync(
        SaleId saleId,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<ProductId, PreviouslyReturned>();
        await using var command = CreateCommand(
            """
            SELECT L.PRODUCT_ID, SUM(L.QUANTITY), SUM(L.NET_RIALS), SUM(L.TAX_RIALS)
            FROM SALE_RETURN_LINE L
            JOIN SALE_RETURN R ON R.ID = L.RETURN_ID
            WHERE R.SALE_ID = @SALE_ID
            GROUP BY L.PRODUCT_ID
            """);
        command.Parameters.Add("@SALE_ID", FbDbType.Char).Value = saleId.ToString();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result[ProductId.From(Guid.Parse(reader.GetString(0)))] = new PreviouslyReturned(
                reader.GetDecimal(1),
                Money.FromRials(reader.GetInt64(2)),
                Money.FromRials(reader.GetInt64(3)));
        }

        return result;
    }

    public async Task<bool> AnyForSaleAsync(SaleId saleId, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand("SELECT FIRST 1 1 FROM SALE_RETURN WHERE SALE_ID = @SALE_ID");
        command.Parameters.Add("@SALE_ID", FbDbType.Char).Value = saleId.ToString();

        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null;
    }

    public async Task<bool> HasRefundedServiceChargeAsync(SaleId saleId, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(
            "SELECT FIRST 1 1 FROM SALE_RETURN WHERE SALE_ID = @SALE_ID AND SERVICE_NET_RIALS > 0");
        command.Parameters.Add("@SALE_ID", FbDbType.Char).Value = saleId.ToString();

        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null;
    }

    private FbCommand CreateCommand(string commandText)
    {
        return new FbCommand(commandText, _unitOfWork.Connection, _unitOfWork.Transaction)
        {
            CommandType = CommandType.Text,
        };
    }
}
