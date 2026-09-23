using System.Data;
using ERP.Application.Customers;
using ERP.Domain.Common;
using ERP.Domain.Customers;
using ERP.Domain.Sales;
using ERP.Persistence.Database;
using ERP.Persistence.Sales;
using FirebirdSql.Data.FirebirdClient;

namespace ERP.Persistence.Customers;

public sealed class FirebirdCustomerLedgerReader : ICustomerLedgerReader
{
    private readonly FirebirdUnitOfWork _unitOfWork;

    public FirebirdCustomerLedgerReader(FirebirdUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CustomerLedgerEntries> ReadAsync(CustomerId customerId, CancellationToken cancellationToken)
    {
        var invoices = new List<CreditInvoice>();
        await using (var command = CreateCommand(
            """
            SELECT NUMBER, COMPLETED_AT_UTC, TOTAL_RIALS
            FROM SALE S
            WHERE CUSTOMER_ID = @CUSTOMER_ID
              AND STATUS = @COMPLETED
              AND PAYMENT_METHOD = @CREDIT
              AND TOTAL_RIALS IS NOT NULL
              -- «صورتحساب اصلاحی» (§10.10) stands in for the sale it corrects —
              -- once corrected, the original stops counting toward the debt, or
              -- the correction's own total would be added on top of it.
              AND NOT EXISTS (SELECT 1 FROM SALE X WHERE X.CORRECTS_SALE_ID = S.ID)
            """))
        {
            command.Parameters.Add("@CUSTOMER_ID", FbDbType.Char).Value = customerId.ToString();
            command.Parameters.Add("@COMPLETED", FbDbType.SmallInt).Value = (short)SaleStatus.Completed;
            command.Parameters.Add("@CREDIT", FbDbType.SmallInt).Value = (short)PaymentMethod.Credit;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                invoices.Add(new CreditInvoice(
                    reader.GetInt64(0),
                    new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc)),
                    Money.FromRials(reader.GetInt64(2))));
            }
        }

        var payments = new List<Money>();
        await using (var command = CreateCommand(
            "SELECT AMOUNT_RIALS FROM CUSTOMER_PAYMENT WHERE CUSTOMER_ID = @CUSTOMER_ID"))
        {
            command.Parameters.Add("@CUSTOMER_ID", FbDbType.Char).Value = customerId.ToString();

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                payments.Add(Money.FromRials(reader.GetInt64(0)));
            }
        }

        // A return refunded «off the customer's debt» lowers what they owe
        // exactly as a payment would; a cash or card refund never touched the
        // account, so it is not counted here.
        await using (var command = CreateCommand(
            $"""
            SELECT {SaleReturnSql.RefundOfR}
            FROM SALE_RETURN R
            WHERE R.CUSTOMER_ID = @CUSTOMER_ID AND R.REFUND_METHOD = @CREDIT
            """))
        {
            command.Parameters.Add("@CUSTOMER_ID", FbDbType.Char).Value = customerId.ToString();
            command.Parameters.Add("@CREDIT", FbDbType.SmallInt).Value = (short)PaymentMethod.Credit;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                payments.Add(Money.FromRials(reader.GetInt64(0)));
            }
        }

        return new CustomerLedgerEntries(invoices, payments);
    }

    private FbCommand CreateCommand(string commandText)
    {
        return new FbCommand(commandText, _unitOfWork.Connection, _unitOfWork.Transaction)
        {
            CommandType = CommandType.Text,
        };
    }
}
