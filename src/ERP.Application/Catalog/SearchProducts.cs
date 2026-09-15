using ERP.Domain.Catalog;
using ERP.Domain.Common;

namespace ERP.Application.Catalog;

public sealed record SearchProductsQuery(string Term, int MaxResults = 8);

public sealed record ProductSearchResult(
    ProductId Id,
    string Name,
    string? Sku,
    string? PrimaryBarcode,
    Money SalePrice);

/// <summary>
/// Reads matching products directly from storage. Implementations are expected to
/// rank prefix matches (name/SKU/barcode starting with the term) ahead of matches
/// found only in the middle of a field, and to apply <see cref="SearchProductsQuery.MaxResults"/>
/// as a database-side LIMIT rather than fetching everything and truncating in memory.
/// </summary>
public interface IProductSearchReader
{
    Task<IReadOnlyList<ProductSearchResult>> SearchAsync(
        SearchProductsQuery query,
        CancellationToken cancellationToken);

    /// <summary>
    /// The active product whose barcode (any of its barcodes) or SKU is exactly
    /// <paramref name="code"/>, or null. Exact, not ranked: a scanner reads a
    /// whole code, and a partial match must never add the wrong product.
    /// </summary>
    Task<ProductSearchResult?> FindByExactCodeAsync(string code, CancellationToken cancellationToken);
}

public interface ISearchProductsHandler
{
    Task<IReadOnlyList<ProductSearchResult>> ExecuteAsync(
        SearchProductsQuery query,
        CancellationToken cancellationToken);

    /// <summary>Scanner path — see <see cref="IProductSearchReader.FindByExactCodeAsync"/>. Blank input finds nothing.</summary>
    Task<ProductSearchResult?> FindByExactCodeAsync(string code, CancellationToken cancellationToken);
}

/// <summary>
/// Backs the cashier's product search box. This is the single highest-frequency
/// action on the busiest screen in the app (Sales & POS), so it is held to the
/// "queue-forming function" standard from competitor UX research written up in
/// the source-of-truth doc's cashier-experience addendum: results for a
/// half-typed word must come back fast, and the closest matches must be on top —
/// not an alphabetical dump the cashier has to scan.
/// </summary>
public sealed class SearchProductsHandler : ISearchProductsHandler
{
    private const int DefaultMaxResults = 8;
    private const int HardResultCap = 50;

    private readonly IProductSearchReader _reader;

    public SearchProductsHandler(IProductSearchReader reader)
    {
        _reader = reader;
    }

    public Task<IReadOnlyList<ProductSearchResult>> ExecuteAsync(
        SearchProductsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var term = query.Term.Trim();
        if (term.Length == 0)
        {
            return Task.FromResult<IReadOnlyList<ProductSearchResult>>([]);
        }

        var boundedMaxResults = Math.Clamp(
            query.MaxResults <= 0 ? DefaultMaxResults : query.MaxResults,
            1,
            HardResultCap);

        return _reader.SearchAsync(
            query with { Term = term, MaxResults = boundedMaxResults },
            cancellationToken);
    }

    public Task<ProductSearchResult?> FindByExactCodeAsync(string code, CancellationToken cancellationToken)
    {
        // Scanners in Persian keyboard layout still send Latin digits, but a
        // code typed by hand may arrive in Persian digits; barcodes and SKUs
        // are stored Latin.
        var normalized = PersianNumber.DigitsToLatin(code?.Trim() ?? string.Empty);
        return normalized.Length == 0
            ? Task.FromResult<ProductSearchResult?>(null)
            : _reader.FindByExactCodeAsync(normalized, cancellationToken);
    }
}
