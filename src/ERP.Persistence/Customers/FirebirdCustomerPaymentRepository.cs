using System.Data;
using ERP.Application.Customers;
using ERP.Domain.Customers;
using ERP.Persistence.Database;
using FirebirdSql.Data.FirebirdClient;

namespace ERP.Persistence.Customers;

/// <summary>Insert-only: a received payment is never edited or deleted (Codex rule 15).</summary>
public sealed class FirebirdCustomerPaymentRepository : ICustomerPaymentRepository
{
    private readonly FirebirdUnitOfWork _unitOfWork;

    public FirebirdCustomerPaymentRepository(FirebirdUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task AddAsync(CustomerPayment payment, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payment);

        await using var command = new FbCommand(
            """
            INSERT INTO CUSTOMER_PAYMENT (ID, CUSTOMER_ID, AMOUNT_RIALS, METHOD, NOTE, RECEIVED_AT_UTC, WAREHOUSE_ID)
            VALUES (@ID, @CUSTOMER_ID, @AMOUNT_RIALS, @METHOD, @NOTE, @RECEIVED_AT_UTC, @WAREHOUSE_ID)
            """,
            _unitOfWork.Connection,
            _unitOfWork.Transaction)
        {
            CommandType = CommandType.Text,
        };
        command.Parameters.Add("@ID", FbDbType.Char).Value = payment.Id.ToString("D");
        command.Parameters.Add("@CUSTOMER_ID", FbDbType.Char).Value = payment.CustomerId.ToString();
        command.Parameters.Add("@AMOUNT_RIALS", FbDbType.BigInt).Value = payment.Amount.Rials;
        command.Parameters.Add("@METHOD", FbDbType.SmallInt).Value = (short)payment.Method;
        command.Parameters.Add("@NOTE", FbDbType.VarChar).Value = payment.Note is { } note ? note : DBNull.Value;
        command.Parameters.Add("@RECEIVED_AT_UTC", FbDbType.TimeStamp).Value = payment.ReceivedAtUtc.UtcDateTime;
        command.Parameters.Add("@WAREHOUSE_ID", FbDbType.Char).Value =
            payment.WarehouseId is { } warehouseId ? warehouseId.ToString() : DBNull.Value;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
