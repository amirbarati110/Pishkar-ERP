using System.Data;
using System.Globalization;
using System.Numerics;
using ERP.Application.Customers;
using ERP.Domain.Common;
using ERP.Domain.Customers;
using ERP.Domain.Sales;
using ERP.Persistence.Database;
using FirebirdSql.Data.FirebirdClient;

namespace ERP.Persistence.Customers;

/// <summary>
/// Backs «لیست مشتریان»: active customers with their balance. The filter, the order, the
/// limit and the footer totals are all decided in the database, so the cost stays flat as
/// the customer book grows and the screen never pulls every customer to cut the list in memory.
///
/// <para>The balance is <c>opening + credit invoices − payments − credit refunds</c> — the same
/// three sources <see cref="FirebirdCustomerLedgerReader"/> reads for one customer. Applying
/// payments oldest-first only decides <i>which</i> invoices stay open; the total owed is just
/// this net (or an advance when it is negative), so a list needs no per-invoice walk.</para>
/// </summary>
public sealed class FirebirdCustomerListReader : ICustomerListReader
{
    /// <summary>Fewer digits than this match too many mobiles / national IDs to be useful; they still match a customer number.</summary>
    private const int MinimumIdentityDigits = 3;

    private const int MaximumCodeDigits = 15;

    private readonly FirebirdConnectionFactory _connectionFactory;

    public FirebirdCustomerListReader(FirebirdConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<CustomerListPage> ListAsync(
        CustomerListQuery query,
        string? numberDigits,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        await using var connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);

        var filter = BuildFilter(query, numberDigits);
        var debtorFilter = query.OnlyDebtors ? "WHERE NET > 0" : string.Empty;
        var rank = query.Term is null ? "0" : $"CASE WHEN {filter.StartsWith} THEN 0 ELSE 1 END";

        var rows = new List<CustomerListRow>();
        await using (var command = new FbCommand(
            filter.Cte + $"""
            SELECT FIRST @TAKE
                   ID, CODE, NAME, MOBILE, ADDRESS, CREDIT_LIMIT_RIALS, OPENING_BALANCE_RIALS, NET,
                   KIND, FIRST_NAME, LAST_NAME, COMPANY_NAME, NATIONAL_ID, ECONOMIC_CODE,
                   REGISTRATION_NUMBER, POSTAL_CODE, PHONE, EMAIL, BIRTH_DATE, NOTES,
                   {rank} AS MATCH_RANK
            FROM BAL
            {debtorFilter}
            ORDER BY MATCH_RANK, NAME
            """,
            connection))
        {
            command.CommandType = CommandType.Text;
            command.Parameters.Add("@TAKE", FbDbType.Integer).Value = query.Take;
            filter.AddParameters(command);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                string? Text(int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal).TrimEnd();

                var net = reader.GetInt64(7);
                var profile = new CustomerProfileInput(
                    (CustomerKind)reader.GetInt16(8),
                    Text(9),
                    Text(10),
                    Text(11),
                    reader.GetString(3),
                    Text(12),
                    Text(13),
                    Text(14),
                    Text(15),
                    Text(16),
                    Text(17),
                    Text(18),
                    Text(4),
                    Text(19));
                rows.Add(new CustomerListRow(
                    CustomerId.From(Guid.Parse(reader.GetString(0))),
                    reader.GetInt64(1),
                    reader.GetString(2),
                    profile,
                    Money.FromRials(reader.GetInt64(5)),
                    Money.FromRials(reader.GetInt64(6)),
                    Money.FromRials(Math.Max(0, net)),
                    Money.FromRials(Math.Max(0, -net))));
            }
        }

        int totalCount;
        long totalDebt;
        long totalAdvance;
        await using (var command = new FbCommand(
            filter.Cte + $"""
            SELECT COUNT(*),
                   COALESCE(SUM(CASE WHEN NET > 0 THEN NET ELSE 0 END), 0),
                   COALESCE(SUM(CASE WHEN NET < 0 THEN -NET ELSE 0 END), 0)
            FROM BAL
            {debtorFilter}
            """,
            connection))
        {
            command.CommandType = CommandType.Text;
            filter.AddParameters(command);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            totalCount = Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture);

            // SUM over BIGINT comes back as a 128-bit integer from Firebird 4 on.
            totalDebt = (long)reader.GetFieldValue<BigInteger>(1);
            totalAdvance = (long)reader.GetFieldValue<BigInteger>(2);
        }

        return new CustomerListPage(rows, totalCount, Money.FromRials(totalDebt), Money.FromRials(totalAdvance));
    }

    private sealed record Filter(string Cte, string StartsWith, Action<FbCommand> AddParameters);

    /// <summary>
    /// The balance and the search in one CTE, so the rows, the count and the debt total can never
    /// disagree. A term matches the name (a company is its own name), a mobile or national-ID
    /// fragment, or a customer number typed exactly.
    /// </summary>
    private static Filter BuildFilter(CustomerListQuery query, string? numberDigits)
    {
        var where = string.Empty;
        var startsWith = "1 = 0";
        Action<FbCommand> addTerm = _ => { };

        if (query.Term is { } term)
        {
            var match = new List<string> { "C.NAME CONTAINING @TERM" };
            var start = new List<string> { "NAME STARTING WITH @TERM" };
            var parameters = new List<Action<FbCommand>> { c => c.Parameters.Add("@TERM", FbDbType.VarChar).Value = term };

            if (numberDigits is { Length: > 0 and <= MaximumCodeDigits } code)
            {
                match.Add("C.CODE = @CODE");
                start.Add("CODE = @CODE");
                parameters.Add(c => c.Parameters.Add("@CODE", FbDbType.BigInt).Value = long.Parse(code, CultureInfo.InvariantCulture));
            }

            if (numberDigits is { Length: >= MinimumIdentityDigits } digits)
            {
                // MOBILE and NATIONAL_ID are CHARACTER SET ASCII: a Persian term bound against them
                // fails in the client, so only the digits-only form is ever sent for them.
                match.Add("C.MOBILE CONTAINING @DIGITS");
                match.Add("C.NATIONAL_ID STARTING WITH @DIGITS");
                start.Add("MOBILE STARTING WITH @DIGITS");
                parameters.Add(c => c.Parameters.Add("@DIGITS", FbDbType.VarChar).Value = digits);
            }

            where = $"AND ({string.Join(" OR ", match)})";
            startsWith = string.Join(" OR ", start);
            addTerm = command => parameters.ForEach(add => add(command));
        }

        var cte =
            $"""
            WITH BAL AS (
                SELECT C.ID, C.CODE, C.NAME, C.MOBILE, C.ADDRESS, C.CREDIT_LIMIT_RIALS, C.OPENING_BALANCE_RIALS,
                       C.KIND, C.FIRST_NAME, C.LAST_NAME, C.COMPANY_NAME, C.NATIONAL_ID, C.ECONOMIC_CODE,
                       C.REGISTRATION_NUMBER, C.POSTAL_CODE, C.PHONE, C.EMAIL, C.BIRTH_DATE, C.NOTES,
                       C.OPENING_BALANCE_RIALS
                       + COALESCE((SELECT SUM(S.TOTAL_RIALS) FROM SALE S
                                   WHERE S.CUSTOMER_ID = C.ID AND S.STATUS = @COMPLETED
                                     AND S.PAYMENT_METHOD = @CREDIT AND S.TOTAL_RIALS IS NOT NULL
                                     AND NOT EXISTS (SELECT 1 FROM SALE X WHERE X.CORRECTS_SALE_ID = S.ID)), 0)
                       - COALESCE((SELECT SUM(P.AMOUNT_RIALS) FROM CUSTOMER_PAYMENT P WHERE P.CUSTOMER_ID = C.ID), 0)
                       - COALESCE((SELECT SUM(L.NET_RIALS + L.TAX_RIALS)
                                   FROM SALE_RETURN R JOIN SALE_RETURN_LINE L ON L.RETURN_ID = R.ID
                                   WHERE R.CUSTOMER_ID = C.ID AND R.REFUND_METHOD = @CREDIT), 0) AS NET
                FROM CUSTOMER C
                WHERE C.STATUS = 1
                  {where}
            )

            """;

        return new Filter(cte, startsWith, command =>
        {
            command.Parameters.Add("@COMPLETED", FbDbType.SmallInt).Value = (short)SaleStatus.Completed;
            command.Parameters.Add("@CREDIT", FbDbType.SmallInt).Value = (short)PaymentMethod.Credit;
            addTerm(command);
        });
    }
}
