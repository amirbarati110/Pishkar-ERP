using ERP.Domain.Catalog;
using ERP.Domain.Common;
using ERP.Domain.Customers;
using ERP.Domain.Inventory;
using ERP.Domain.Sales;

namespace ERP.Application.Sales;

/// <summary>What a cart row shows about its product beyond what the sale itself stores.</summary>
/// <param name="Available">
/// Stock on hand in the sale's warehouse right now — negative when goods were
/// sold ahead of being received (see <see cref="StockBalance"/>).
/// </param>
public sealed record SaleProductInfo(
    ProductId ProductId,
    string Name,
    string? Sku,
    string UnitSymbol,
    StockBalance Available);

/// <summary>One invoice in a list — «فاکتورهای امروز» or «فاکتورهای معلق».</summary>
/// <param name="Amount">
/// For a completed invoice, the payable total fixed at completion (null for
/// invoices completed before totals were stored). For a draft, the goods
/// subtotal so far, since its tax is only fixed when it is completed.
/// </param>
public sealed record SaleListItem(
    SaleId SaleId,
    SaleNumber? Number,
    DateTimeOffset OpenedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    CustomerId? CustomerId,
    string? CustomerName,
    int ItemCount,
    Money? Amount,
    PaymentMethod? PaymentMethod);

/// <summary>
/// Read side for the sales screen. Lists are ranked and filtered in the
/// database; nothing here loads every sale into memory.
/// </summary>
public interface ISaleReadReader
{
    Task<IReadOnlyDictionary<ProductId, SaleProductInfo>> ReadProductsAsync(
        WarehouseId warehouseId,
        IReadOnlyCollection<ProductId> productIds,
        CancellationToken cancellationToken);

    /// <summary>Completed invoices with <paramref name="fromUtc"/> ≤ completion &lt; <paramref name="toUtc"/>, newest number first.</summary>
    Task<IReadOnlyList<SaleListItem>> ListCompletedAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken);

    /// <summary>Draft invoices that have at least one item, oldest first.</summary>
    Task<IReadOnlyList<SaleListItem>> ListDraftsWithItemsAsync(CancellationToken cancellationToken);

    /// <summary>One page of the product list, with the total number of matching products.</summary>
    Task<(IReadOnlyList<SaleProductListItem> Items, int TotalCount)> BrowseProductsAsync(
        ProductListCriteria criteria,
        CancellationToken cancellationToken);
}
