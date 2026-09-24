using System.Data;
using System.Globalization;
using ERP.Application.Customers;
using ERP.Domain.Common;
using ERP.Domain.Customers;
using ERP.Domain.Inventory;
using ERP.Persistence.Database;
using FirebirdSql.Data.FirebirdClient;

namespace ERP.Persistence.Customers;

/// <summary>Backs a cash shift's «دریافت نقدی از مشتریان»: cash payments taken at one till in a window.</summary>
public sealed class FirebirdCustomerCashReceiptReader : ICustomerCashReceiptReader
{
    private readonly FirebirdUnitOfWork _unitOfWork;

    public FirebirdCustomerCashReceiptReader(FirebirdUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Money> SumCashReceivedAsync(
        WarehouseId warehouseId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken)
    {
        await using var command = new FbCommand(
            """
            SELECT CAST(COALESCE(SUM(AMOUNT_RIALS), 0) AS BIGINT)
            FROM CUSTOMER_PAYMENT
            WHERE WAREHOUSE_ID = @WAREHOUSE_ID AND METHOD = @CASH
              AND RECEIVED_AT_UTC >= @FROM_UTC AND RECEIVED_AT_UTC < @TO_UTC
            """,
            _unitOfWork.Connection,
            _unitOfWork.Transaction)
        {
            CommandType = CommandType.Text,
        };
        command.Parameters.Add("@WAREHOUSE_ID", FbDbType.Char).Value = warehouseId.ToString();
        command.Parameters.Add("@CASH", FbDbType.SmallInt).Value = (short)CustomerPaymentMethod.Cash;
        command.Parameters.Add("@FROM_UTC", FbDbType.TimeStamp).Value = fromUtc.UtcDateTime;
        command.Parameters.Add("@TO_UTC", FbDbType.TimeStamp).Value = toUtc.UtcDateTime;

        var total = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Money.FromRials(Convert.ToInt64(total, CultureInfo.InvariantCulture));
    }
}
