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
        """
        ID, NAME, MOBILE, ADDRESS, CREDIT_LIMIT_RIALS, OPENING_BALANCE_RIALS, STATUS,
        CODE, KIND, FIRST_NAME, LAST_NAME, COMPANY_NAME, NATIONAL_ID, ECONOMIC_CODE,
        REGISTRATION_NUMBER, POSTAL_CODE, PHONE, EMAIL, BIRTH_DATE, NOTES
        """;

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

    public async Task<Customer?> FindByNationalIdAsync(string normalizedNationalId, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand($"SELECT {SelectColumns} FROM CUSTOMER WHERE NATIONAL_ID = @NATIONAL_ID");
        command.Parameters.Add("@NATIONAL_ID", FbDbType.VarChar).Value = normalizedNationalId;
        return await ReadSingleAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task AddAsync(Customer customer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(customer);

        // The number is taken inside the same transaction as the insert; a sequence never hands the
        // same value out twice, so two tills creating customers at once get different numbers.
        await using (var next = CreateCommand("SELECT NEXT VALUE FOR SEQ_CUSTOMER_CODE FROM RDB$DATABASE"))
        {
            var code = Convert.ToInt64(
                await next.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
                System.Globalization.CultureInfo.InvariantCulture);
            customer.AssignCode(code);
        }

        await using var command = CreateCommand(
            """
            INSERT INTO CUSTOMER (
                ID, NAME, MOBILE, ADDRESS, CREDIT_LIMIT_RIALS, OPENING_BALANCE_RIALS, STATUS,
                CODE, KIND, FIRST_NAME, LAST_NAME, COMPANY_NAME, NATIONAL_ID, ECONOMIC_CODE,
                REGISTRATION_NUMBER, POSTAL_CODE, PHONE, EMAIL, BIRTH_DATE, NOTES)
            VALUES (
                @ID, @NAME, @MOBILE, @ADDRESS, @CREDIT_LIMIT_RIALS, @OPENING_BALANCE_RIALS, @STATUS,
                @CODE, @KIND, @FIRST_NAME, @LAST_NAME, @COMPANY_NAME, @NATIONAL_ID, @ECONOMIC_CODE,
                @REGISTRATION_NUMBER, @POSTAL_CODE, @PHONE, @EMAIL, @BIRTH_DATE, @NOTES)
            """);
        command.Parameters.Add("@ID", FbDbType.Char).Value = customer.Id.ToString();
        command.Parameters.Add("@OPENING_BALANCE_RIALS", FbDbType.BigInt).Value = customer.OpeningBalance.Rials;
        command.Parameters.Add("@CODE", FbDbType.BigInt).Value = customer.Code;
        AddSharedParameters(command, customer);

        await WriteAsync(command, "این شماره موبایل یا کد ملی همین حالا برای مشتری دیگری ثبت شد؛ مشتری را جست‌وجو و انتخاب کنید.", cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task UpdateAsync(Customer customer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(customer);

        // Not written: the code (fixed for life), the opening balance (would rewrite every balance).
        await using var command = CreateCommand(
            """
            UPDATE CUSTOMER
            SET NAME = @NAME, MOBILE = @MOBILE, ADDRESS = @ADDRESS,
                CREDIT_LIMIT_RIALS = @CREDIT_LIMIT_RIALS, STATUS = @STATUS,
                KIND = @KIND, FIRST_NAME = @FIRST_NAME, LAST_NAME = @LAST_NAME, COMPANY_NAME = @COMPANY_NAME,
                NATIONAL_ID = @NATIONAL_ID, ECONOMIC_CODE = @ECONOMIC_CODE,
                REGISTRATION_NUMBER = @REGISTRATION_NUMBER, POSTAL_CODE = @POSTAL_CODE,
                PHONE = @PHONE, EMAIL = @EMAIL, BIRTH_DATE = @BIRTH_DATE, NOTES = @NOTES
            WHERE ID = @ID
            """);
        command.Parameters.Add("@ID", FbDbType.Char).Value = customer.Id.ToString();
        AddSharedParameters(command, customer);

        await WriteAsync(command, "این شماره موبایل یا کد ملی همین حالا برای مشتری دیگری ثبت شد.", cancellationToken)
            .ConfigureAwait(false);
    }

    private static void AddSharedParameters(FbCommand command, Customer customer)
    {
        var profile = customer.Profile;
        command.Parameters.Add("@NAME", FbDbType.VarChar).Value = profile.DisplayName;
        command.Parameters.Add("@MOBILE", FbDbType.Char).Value = profile.Mobile;
        command.Parameters.Add("@ADDRESS", FbDbType.VarChar).Value = Nullable(profile.Address);
        command.Parameters.Add("@CREDIT_LIMIT_RIALS", FbDbType.BigInt).Value = customer.CreditLimit.Rials;
        command.Parameters.Add("@STATUS", FbDbType.SmallInt).Value = (short)customer.Status;
        command.Parameters.Add("@KIND", FbDbType.SmallInt).Value = (short)profile.Kind;
        command.Parameters.Add("@FIRST_NAME", FbDbType.VarChar).Value = Nullable(profile.FirstName);
        command.Parameters.Add("@LAST_NAME", FbDbType.VarChar).Value = Nullable(profile.LastName);
        command.Parameters.Add("@COMPANY_NAME", FbDbType.VarChar).Value = Nullable(profile.CompanyName);
        command.Parameters.Add("@NATIONAL_ID", FbDbType.VarChar).Value = Nullable(profile.NationalId);
        command.Parameters.Add("@ECONOMIC_CODE", FbDbType.VarChar).Value = Nullable(profile.EconomicCode);
        command.Parameters.Add("@REGISTRATION_NUMBER", FbDbType.VarChar).Value = Nullable(profile.RegistrationNumber);
        command.Parameters.Add("@POSTAL_CODE", FbDbType.Char).Value = Nullable(profile.PostalCode);
        command.Parameters.Add("@PHONE", FbDbType.VarChar).Value = Nullable(profile.Phone);
        command.Parameters.Add("@EMAIL", FbDbType.VarChar).Value = Nullable(profile.Email);
        command.Parameters.Add("@BIRTH_DATE", FbDbType.Char).Value = Nullable(profile.BirthDate is { } birth ? PersianNumber.DigitsToLatin(birth.ToString()) : null);
        command.Parameters.Add("@NOTES", FbDbType.VarChar).Value = Nullable(profile.Notes);
    }

    private static object Nullable(string? value) => value is null ? DBNull.Value : value;

    private static async Task WriteAsync(FbCommand command, string duplicateMessage, CancellationToken cancellationToken)
    {
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (FbException exception) when (FirebirdConstraint.IsViolation(exception, "UQ_CUSTOMER_MOBILE"))
        {
            throw new DataConflictException("customers.customer.duplicate-mobile", duplicateMessage, exception);
        }
        catch (FbException exception) when (FirebirdConstraint.IsViolation(exception, "UQ_CUSTOMER_NATIONAL_ID"))
        {
            throw new DataConflictException("customers.customer.duplicate-national-id", duplicateMessage, exception);
        }
    }

    private static async Task<Customer?> ReadSingleAsync(FbCommand command, CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return Rehydrate(reader);
    }

    /// <summary>Builds a customer from a row in <see cref="SelectColumns"/> order.</summary>
    private static Customer Rehydrate(FbDataReader reader)
    {
        string? Text(int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal).TrimEnd();

        PersianDate? birthDate = null;
        if (Text(18) is { } birth && TryParseBirthDate(birth, out var parsed))
        {
            birthDate = parsed;
        }

        var profile = CustomerProfile.Rehydrate(
            (CustomerKind)reader.GetInt16(8),
            reader.GetString(1),
            Text(9),
            Text(10),
            Text(11),
            reader.GetString(2),
            Text(12),
            Text(13),
            Text(14),
            Text(15),
            Text(16),
            Text(17),
            birthDate,
            Text(3),
            Text(19));

        return Customer.Rehydrate(
            CustomerId.From(Guid.Parse(reader.GetString(0))),
            reader.GetInt64(7),
            profile,
            Money.FromRials(reader.GetInt64(4)),
            Money.FromRials(reader.GetInt64(5)),
            (CustomerStatus)reader.GetInt16(6));
    }

    private static bool TryParseBirthDate(string text, out PersianDate? date)
    {
        date = null;
        var parts = text.Split('/');
        if (parts.Length == 3 && int.TryParse(parts[0], out var y) && int.TryParse(parts[1], out var m) && int.TryParse(parts[2], out var d))
        {
            try
            {
                date = PersianDate.Create(y, m, d);
                return true;
            }
            catch (DomainException)
            {
                return false;
            }
        }

        return false;
    }

    private FbCommand CreateCommand(string commandText)
    {
        return new FbCommand(commandText, _unitOfWork.Connection, _unitOfWork.Transaction)
        {
            CommandType = CommandType.Text,
        };
    }
}
