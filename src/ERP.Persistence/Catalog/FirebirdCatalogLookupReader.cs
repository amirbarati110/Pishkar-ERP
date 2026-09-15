using ERP.Application.Catalog;
using ERP.Domain.Catalog;
using ERP.Persistence.Database;
using FirebirdSql.Data.FirebirdClient;

namespace ERP.Persistence.Catalog;

public sealed class FirebirdCatalogLookupReader : ICatalogLookupReader
{
    private readonly FirebirdConnectionFactory _connectionFactory;

    public FirebirdCatalogLookupReader(FirebirdConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<CatalogLookupSnapshot> LoadAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        var categories = await ReadCategoriesAsync(connection, cancellationToken).ConfigureAwait(false);
        var units = await ReadUnitsAsync(connection, cancellationToken).ConfigureAwait(false);
        var products = await ReadProductsAsync(connection, cancellationToken).ConfigureAwait(false);

        return new CatalogLookupSnapshot(categories, units, products);
    }

    private static async Task<IReadOnlyList<CategoryLookupItem>> ReadCategoriesAsync(
        FbConnection connection,
        CancellationToken cancellationToken)
    {
        var items = new List<CategoryLookupItem>();
        await using var command = new FbCommand(
            """
            SELECT ID, NAME, PARENT_ID, SORT_ORDER
            FROM CATEGORY
            WHERE STATUS = 1
            ORDER BY SORT_ORDER, NAME
            """,
            connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            items.Add(new CategoryLookupItem(
                CategoryId.From(Guid.Parse(reader.GetString(0))),
                reader.GetString(1),
                reader.IsDBNull(2)
                    ? null
                    : CategoryId.From(Guid.Parse(reader.GetString(2))),
                reader.GetInt32(3)));
        }

        return items;
    }

    private static async Task<IReadOnlyList<UnitLookupItem>> ReadUnitsAsync(
        FbConnection connection,
        CancellationToken cancellationToken)
    {
        var items = new List<UnitLookupItem>();
        await using var command = new FbCommand(
            """
            SELECT ID, NAME, SYMBOL, ALLOWS_FRACTIONS
            FROM PRODUCT_UNIT
            ORDER BY NAME
            """,
            connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            items.Add(new UnitLookupItem(
                UnitId.From(Guid.Parse(reader.GetString(0))),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetBoolean(3)));
        }

        return items;
    }

    private static async Task<IReadOnlyList<ProductLookupItem>> ReadProductsAsync(
        FbConnection connection,
        CancellationToken cancellationToken)
    {
        var items = new List<ProductLookupItem>();
        await using var command = new FbCommand(
            """
            SELECT P.ID, P.NAME, P.SKU,
                   (SELECT FIRST 1 B.BARCODE
                    FROM PRODUCT_BARCODE B
                    WHERE B.PRODUCT_ID = P.ID
                    ORDER BY B.BARCODE)
            FROM PRODUCT P
            WHERE P.STATUS = 1
            ORDER BY P.NAME
            """,
            connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            items.Add(new ProductLookupItem(
                ProductId.From(Guid.Parse(reader.GetString(0))),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3)));
        }

        return items;
    }
}
