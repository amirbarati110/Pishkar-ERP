using ERP.Application.Common;
using ERP.Domain.Catalog;
using ERP.Domain.Common;
using ERP.Domain.Inventory;

namespace ERP.Application.Sales;

/// <summary>The three lists above the product table on the sales screen.</summary>
public enum ProductListFilter
{
    /// <summary>«همه کالاها», alphabetical.</summary>
    All = 1,

    /// <summary>«پر فروش‌ها»: what sold most in this warehouse recently, most first.</summary>
    TopSelling = 2,

    /// <summary>«موجودی کم»: what is about to run out, emptiest first.</summary>
    LowStock = 3,
}

/// <param name="CategoryId">A category chip; its sub-categories are included. Null is «همه».</param>
public sealed record BrowseProductsForSaleQuery(
    WarehouseId WarehouseId,
    CategoryId? CategoryId,
    ProductListFilter Filter,
    int Page = 1,
    int PageSize = 15);

public sealed record SaleProductListItem(
    ProductId Id,
    string Name,
    string? Sku,
    string UnitSymbol,
    Money SalePrice,
    StockBalance Available);

public sealed record SaleProductListPage(
    IReadOnlyList<SaleProductListItem> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    public int PageCount => TotalCount == 0 ? 1 : (TotalCount + PageSize - 1) / PageSize;
}

/// <summary>What storage needs to build one page of the list.</summary>
public sealed record ProductListCriteria(
    WarehouseId WarehouseId,
    CategoryId? CategoryId,
    ProductListFilter Filter,
    DateTimeOffset SoldSinceUtc,
    decimal LowStockAtOrBelow,
    int Offset,
    int Limit);

public interface IBrowseProductsForSaleHandler
{
    Task<SaleProductListPage> ExecuteAsync(BrowseProductsForSaleQuery query, CancellationToken cancellationToken);
}

/// <summary>
/// The middle product list and the category tiles of the sales screen, paged
/// and filtered in the database. Only active products in categories visible
/// at the till (§5.3 «Visibility») are offered.
///
/// Typed search is deliberately not part of this: the search box uses
/// <see cref="Catalog.ISearchProductsHandler"/>, the one ranked search of
/// appendix B, so there are not two definitions of «what matches».
/// </summary>
public sealed class BrowseProductsForSaleHandler : IBrowseProductsForSaleHandler
{
    /// <summary>How far back «پر فروش‌ها» looks. Long enough to be stable, short enough to follow the season.</summary>
    public static readonly TimeSpan TopSellingWindow = TimeSpan.FromDays(30);

    /// <summary>
    /// «موجودی کم» until products have their own reorder point (§8.5, Phase 2
    /// purchasing): one shop-wide threshold, in each product's own unit.
    /// </summary>
    public const decimal LowStockThreshold = 10;

    private const int MaximumPageSize = 100;

    private readonly ISaleReadReader _reader;
    private readonly IClock _clock;

    public BrowseProductsForSaleHandler(ISaleReadReader reader, IClock clock)
    {
        _reader = reader;
        _clock = clock;
    }

    public async Task<SaleProductListPage> ExecuteAsync(
        BrowseProductsForSaleQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var pageSize = Math.Clamp(query.PageSize, 1, MaximumPageSize);
        var page = Math.Max(1, query.Page);

        var (items, total) = await _reader
            .BrowseProductsAsync(
                new ProductListCriteria(
                    query.WarehouseId,
                    query.CategoryId,
                    query.Filter,
                    _clock.UtcNow - TopSellingWindow,
                    LowStockThreshold,
                    (page - 1) * pageSize,
                    pageSize),
                cancellationToken)
            .ConfigureAwait(false);

        return new SaleProductListPage(items, page, pageSize, total);
    }
}
