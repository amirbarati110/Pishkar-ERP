using System.Data;
using ERP.Application.Common;
using ERP.Application.Customers;
using ERP.Domain.Common;
using ERP.Domain.Customers;
using ERP.Persistence.Database;
using FirebirdSql.Data.FirebirdClient;

namespace ERP.Persistence.Customers;

public sealed class FirebirdCustomerRepository : ICustomerRepository
{
    private const string SelectColumns =
        "ID, NAME, MOBILE, ADDRESS, CREDIT_LIMIT_RIALS, OPENING_BALANCE_RIALS, STATUS";

    private readonly FirebirdUnitOfWork _unitOfWork;

    public FirebirdCustomerRepository(FirebirdUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Customer?> GetByIdAsync(CustomerId customerId, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand($"SELECT {SelectColumns} FROM CUSTOMER WHERE ID = @ID");
        command.Parameters.Add("@ID", FbDbType.Char).Value = customerId.ToString();
        return await ReadSingleAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Customer?> FindByMobileAsync(string normalizedMobile, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand($"SELECT {SelectColumns} FROM CUSTOMER WHERE MOBILE = @MOBILE");
        command.Parameters.Add("@MOBILE", FbDbType.Char).Value = normalizedMobile;
        return await ReadSingleAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task AddAsync(Customer customer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(customer);

        await using var command = CreateCommand(
            """
            INSERT INTO CUSTOMER (
                ID, NAME, MOBILE, ADDRESS, CREDIT_LIMIT_RIALS, OPENING_BALANCE_RIALS, STATUS)
            VALUES (
                @ID, @NAME, @MOBILE, @ADDRESS, @CREDIT_LIMIT_RIALS, @OPENING_BALANCE_RIALS, @STATUS)
            """);
        command.Parameters.Add("@ID", FbDbType.Char).Value = customer.Id.ToString();
        command.Parameters.Add("@NAME", FbDbType.VarChar).Value = customer.Name;
        command.Parameters.Add("@MOBILE", FbDbType.Char).Value = customer.Mobile;
        command.Parameters.Add("@ADDRESS", FbDbType.VarChar).Value =
            customer.Address is { } address ? address : DBNull.Value;
        command.Parameters.Add("@CREDIT_LIMIT_RIALS", FbDbType.BigInt).Value = customer.CreditLimit.Rials;
        command.Parameters.Add("@OPENING_BALANCE_RIALS", FbDbType.BigInt).Value = customer.OpeningBalance.Rials;
        command.Parameters.Add("@STATUS", FbDbType.SmallInt).Value = (short)customer.Status;

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (FbException exception) when (FirebirdConstraint.IsViolation(exception, "UQ_CUSTOMER_MOBILE"))
        {
            throw new DataConflictException(
                "customers.customer.duplicate-mobile",
                "این شماره موبایل همین حالا برای مشتری دیگری ثبت شد؛ مشتری را جست‌وجو و انتخاب کنید.",
                exception);
        }
    }

    private static async Task<Customer?> ReadSingleAsync(FbCommand command, CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return Customer.Rehydrate(
            CustomerId.From(Guid.Parse(reader.GetString(0))),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            Money.FromRials(reader.GetInt64(4)),
            Money.FromRials(reader.GetInt64(5)),
            (CustomerStatus)reader.GetInt16(6));
    }

    private FbCommand CreateCommand(string commandText)
    {
        return new FbCommand(commandText, _unitOfWork.Connection, _unitOfWork.Transaction)
        {
            CommandType = CommandType.Text,
        };
    }
}
