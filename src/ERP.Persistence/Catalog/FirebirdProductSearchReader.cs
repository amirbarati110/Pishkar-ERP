using System.Data;
using ERP.Application.Catalog;
using ERP.Domain.Catalog;
using ERP.Domain.Common;
using ERP.Persistence.Database;
using FirebirdSql.Data.FirebirdClient;

namespace ERP.Persistence.Catalog;

/// <summary>
/// Backs the cashier's fast product search (source-of-truth cashier-experience
/// addendum). Ranks matches in one query rather than fetching everything and
/// sorting in memory, so latency stays flat as the catalog grows:
///
///   0 — name, SKU, or a barcode STARTS WITH the term (what a cashier typing the
///       first few letters of a product, or the first digits of a scanned
///       barcode, expects to see first)
///   1 — the term appears anywhere else in the name, SKU, or a barcode
///
/// STARTING WITH is index-friendly against IX_PRODUCT_NAME; CONTAINING (Firebird's
/// case-insensitive substring operator) covers the "typed the middle of the word"
/// case at the cost of a scan, which is acceptable at retail-catalog scale.
/// </summary>
public sealed class FirebirdProductSearchReader : IProductSearchReader
{
    private readonly FirebirdConnectionFactory _connectionFactory;

    public FirebirdProductSearchReader(FirebirdConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<ProductSearchResult>> SearchAsync(
        SearchProductsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        await using var connection = await _connectionFactory
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        await using var command = new FbCommand(
            """
            SELECT FIRST @MAX_RESULTS
                   P.ID, P.NAME, P.SKU, P.SALE_PRICE_RIALS,
                   (SELECT FIRST 1 B.BARCODE
                    FROM PRODUCT_BARCODE B
                    WHERE B.PRODUCT_ID = P.ID
                    ORDER BY B.BARCODE) AS PRIMARY_BARCODE,
                   CASE
                       WHEN P.NAME STARTING WITH @TERM THEN 0
                       WHEN P.SKU STARTING WITH @TERM THEN 0
                       WHEN EXISTS (
                           SELECT 1 FROM PRODUCT_BARCODE MB
                           WHERE MB.PRODUCT_ID = P.ID AND MB.BARCODE STARTING WITH @ASCII_TERM)
                           THEN 0
                       ELSE 1
                   END AS MATCH_RANK
            FROM PRODUCT P
            WHERE P.STATUS = 1
              AND (
                  P.NAME CONTAINING @TERM
                  OR P.SKU CONTAINING @TERM
                  OR EXISTS (
                      SELECT 1 FROM PRODUCT_BARCODE MB
                      WHERE MB.PRODUCT_ID = P.ID AND MB.BARCODE CONTAINING @ASCII_TERM)
              )
            ORDER BY MATCH_RANK, P.NAME
            """,
            connection)
        {
            CommandType = CommandType.Text,
        };
        command.Parameters.Add("@MAX_RESULTS", FbDbType.Integer).Value = query.MaxResults;
        command.Parameters.Add("@TERM", FbDbType.VarChar).Value = query.Term;

        // PRODUCT_BARCODE.BARCODE is CHARACTER SET ASCII. Binding a term with
        // non-ASCII (e.g. Persian) characters against it throws a Firebird
        // transliteration error at the client level, before any row is even
        // evaluated — it is not simply "no match". Barcodes are always ASCII in
        // practice, so a non-ASCII term can never legitimately match one; pass
        // DBNull instead, which makes both STARTING WITH/CONTAINING evaluate to
        // NULL (falsy) for that branch, safely excluding barcode matching
        // without touching the name/SKU matching above.
        var isAsciiTerm = query.Term.All(character => character <= 127);
        command.Parameters.Add("@ASCII_TERM", FbDbType.VarChar).Value =
            isAsciiTerm ? query.Term : DBNull.Value;

        var results = new List<ProductSearchResult>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new ProductSearchResult(
                ProductId.From(Guid.Parse(reader.GetString(0))),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                Money.FromRials(reader.GetInt64(3))));
        }

        return results;
    }
}
