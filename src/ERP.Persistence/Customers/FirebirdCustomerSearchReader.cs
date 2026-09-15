using System.Data;
using ERP.Application.Customers;
using ERP.Domain.Customers;
using ERP.Persistence.Database;
using FirebirdSql.Data.FirebirdClient;

namespace ERP.Persistence.Customers;

/// <summary>
/// Ranks in one query, like <see cref="Catalog.FirebirdProductSearchReader"/>:
///
///   0 — name or mobile STARTS WITH the term
///   1 — the term appears elsewhere in the name or mobile
///
/// Archived customers are left out: the sales screen must not offer someone
/// who can no longer buy on account.
/// </summary>
public sealed class FirebirdCustomerSearchReader : ICustomerSearchReader
{
    private readonly FirebirdConnectionFactory _connectionFactory;

    public FirebirdCustomerSearchReader(FirebirdConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<CustomerSearchResult>> SearchAsync(
        string nameTerm,
        string? mobileDigits,
        int maxResults,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new FbCommand(
            """
            SELECT FIRST @MAX_RESULTS
                   C.ID, C.NAME, C.MOBILE,
                   CASE
                       WHEN C.NAME STARTING WITH @NAME_TERM THEN 0
                       WHEN C.MOBILE STARTING WITH @MOBILE_DIGITS THEN 0
                       ELSE 1
                   END AS MATCH_RANK
            FROM CUSTOMER C
            WHERE C.STATUS = 1
              AND (C.NAME CONTAINING @NAME_TERM OR C.MOBILE CONTAINING @MOBILE_DIGITS)
            ORDER BY MATCH_RANK, C.NAME
            """,
            connection)
        {
            CommandType = CommandType.Text,
        };
        command.Parameters.Add("@MAX_RESULTS", FbDbType.Integer).Value = maxResults;
        command.Parameters.Add("@NAME_TERM", FbDbType.VarChar).Value = nameTerm;

        // MOBILE is CHARACTER SET ASCII; a NULL parameter makes both mobile
        // predicates NULL (falsy), so a name-only search never touches it.
        command.Parameters.Add("@MOBILE_DIGITS", FbDbType.VarChar).Value =
            mobileDigits is { } digits ? digits : DBNull.Value;

        var results = new List<CustomerSearchResult>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new CustomerSearchResult(
                CustomerId.From(Guid.Parse(reader.GetString(0))),
                reader.GetString(1),
                reader.GetString(2)));
        }

        return results;
    }
}
