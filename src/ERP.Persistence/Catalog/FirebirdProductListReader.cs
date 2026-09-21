using System.Data;
using System.Globalization;
using System.Text;
using ERP.Application.Catalog;
using ERP.Domain.Catalog;
using ERP.Domain.Common;
using ERP.Persistence.Database;
using FirebirdSql.Data.FirebirdClient;

namespace ERP.Persistence.Catalog;

/// <summary>
/// Backs «لیست کالاها»: active products with their category, unit and stock on hand.
/// Everything is decided in the database — the filter, the order, the limit and the
/// two footer totals — so the cost stays flat as the catalog grows (§۱۵.۲۳ #۱۲); the
/// screen never fetches the whole catalog to cut it down in memory.
/// </summary>
public sealed class FirebirdProductListReader : IProductListReader
{
    private readonly FirebirdConnectionFactory _connectionFactory;

    public FirebirdProductListReader(FirebirdConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<ProductListPage> ListAsync(ProductListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        await using var connection = await _connectionFactory
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        var (cte, where) = BuildFilter(query);
        var rank = query.Term is null
            ? "0"
            : "CASE WHEN P.NAME STARTING WITH @TERM THEN 0 WHEN P.SKU STARTING WITH @TERM THEN 0 ELSE 1 END";

        var rows = new List<ProductListRow>();
        await using (var command = new FbCommand(
            cte + $"""
            SELECT FIRST @TAKE
                   P.ID, P.SKU, P.NAME, P.CATEGORY_ID, C.NAME, P.BASE_UNIT_ID, U.SYMBOL, P.SALE_PRICE_RIALS,
                   COALESCE((SELECT SUM(L.REMAINING_QTY) FROM INVENTORY_LAYER L WHERE L.PRODUCT_ID = P.ID), 0),
                   (SELECT FIRST 1 B.BARCODE FROM PRODUCT_BARCODE B WHERE B.PRODUCT_ID = P.ID ORDER BY B.BARCODE),
                   {rank} AS MATCH_RANK
            FROM PRODUCT P
            JOIN CATEGORY C ON C.ID = P.CATEGORY_ID
            JOIN PRODUCT_UNIT U ON U.ID = P.BASE_UNIT_ID
            {where}
            ORDER BY MATCH_RANK, P.NAME
            """,
            connection))
        {
            command.CommandType = CommandType.Text;
            command.Parameters.Add("@TAKE", FbDbType.Integer).Value = query.Take;
            AddFilterParameters(command, query);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                rows.Add(new ProductListRow(
                    ProductId.From(Guid.Parse(reader.GetString(0))),
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.GetString(2),
                    CategoryId.From(Guid.Parse(reader.GetString(3))),
                    reader.GetString(4),
                    UnitId.From(Guid.Parse(reader.GetString(5))),
                    reader.GetString(6),
                    Money.FromRials(reader.GetInt64(7)),
                    reader.GetDecimal(8),
                    reader.IsDBNull(9) ? null : reader.GetString(9)));
            }
        }

        int totalCount;
        await using (var command = new FbCommand(
            cte + $"SELECT COUNT(*) FROM PRODUCT P {where}",
            connection))
        {
            command.CommandType = CommandType.Text;
            AddFilterParameters(command, query);
            totalCount = Convert.ToInt32(
                await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
                CultureInfo.InvariantCulture);
        }

        decimal totalStock;
        await using (var command = new FbCommand(
            cte + $"""
            SELECT COALESCE(SUM(L.REMAINING_QTY), 0)
            FROM INVENTORY_LAYER L
            JOIN PRODUCT P ON P.ID = L.PRODUCT_ID
            {where}
            """,
            connection))
        {
            command.CommandType = CommandType.Text;
            AddFilterParameters(command, query);
            totalStock = Convert.ToDecimal(
                await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
                CultureInfo.InvariantCulture);
        }

        return new ProductListPage(rows, totalCount, totalStock);
    }

    /// <summary>
    /// The category filter walks the tree down (a recursive CTE), so choosing «خشکبار»
    /// also lists «گردو» and «گردو ایرانی» below it. The term matches name, code or any
    /// barcode anywhere in the text — the half-word search the cashier relies on.
    /// </summary>
    private static (string Cte, string Where) BuildFilter(ProductListQuery query)
    {
        var cte = new StringBuilder();
        var where = new StringBuilder("WHERE P.STATUS = 1");

        if (query.CategoryId is not null)
        {
            cte.Append(
                """
                WITH RECURSIVE TREE (ID) AS (
                    SELECT ID FROM CATEGORY WHERE ID = @CATEGORY_ID
                    UNION ALL
                    SELECT C2.ID FROM CATEGORY C2 JOIN TREE T ON C2.PARENT_ID = T.ID
                )

                """);
            where.Append(" AND P.CATEGORY_ID IN (SELECT ID FROM TREE)");
        }

        if (query.Term is not null)
        {
            where.Append(
                """

                  AND (
                      P.NAME CONTAINING @TERM
                      OR P.SKU CONTAINING @TERM
                      OR EXISTS (
                          SELECT 1 FROM PRODUCT_BARCODE MB
                          WHERE MB.PRODUCT_ID = P.ID AND MB.BARCODE CONTAINING @ASCII_TERM)
                  )
                """);
        }

        return (cte.ToString(), where.ToString());
    }

    private static void AddFilterParameters(FbCommand command, ProductListQuery query)
    {
        if (query.CategoryId is { } categoryId)
        {
            command.Parameters.Add("@CATEGORY_ID", FbDbType.Char).Value = categoryId.ToString();
        }

        if (query.Term is { } term)
        {
            command.Parameters.Add("@TERM", FbDbType.VarChar).Value = term;

            // PRODUCT_BARCODE.BARCODE is CHARACTER SET ASCII: a Persian term bound against it
            // fails in the client before any row is read (see FirebirdProductSearchReader).
            // No barcode contains such a character, so that branch just gets NULL.
            command.Parameters.Add("@ASCII_TERM", FbDbType.VarChar).Value =
                term.All(character => character <= 127) ? term : DBNull.Value;
        }
    }
}
