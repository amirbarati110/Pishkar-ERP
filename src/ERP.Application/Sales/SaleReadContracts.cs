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

/// <summary>
/// What the pencil window shows next to the price field (§6.10) — everything
/// real that can be known from stock and past sales, nothing invented.
/// </summary>
/// <param name="LastPurchaseCost">
/// The most recently received layer's unit cost, null if this product was
/// never received into this warehouse.
/// </param>
/// <param name="CurrentCost">
/// The unit cost of the layer FIFO will actually draw from next — the oldest
/// layer that still has stock — so the margin shown matches what completing
/// the sale will really cost. Null when there is no stock to draw from.
/// </param>
/// <param name="LastSalePriceToCustomer">
/// The unit price this exact customer last paid for this product on a
/// completed invoice, or null if there is no such sale (including when the
/// invoice has no customer — «مشتری نقدی» has no history to compare against).
/// </param>
public sealed record LineEditInfo(
    Money? LastPurchaseCost,
    Money? CurrentCost,
    Money? LastSalePriceToCustomer);

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

    /// <summary>Completed invoices with <paramref name="fromUtc"/> ≤ completion &lt; <paramref name="toUtc"/>, newest number first — every warehouse (the workbench's «امروز» is store-wide on purpose).</summary>
    Task<IReadOnlyList<SaleListItem>> ListCompletedAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken);

    /// <summary>Same as <see cref="ListCompletedAsync"/>, restricted to one warehouse — for anything scoped to a single till/warehouse, like a cash shift's own reconciliation (checklist appendix ز.۶: the store-wide version double-counts once a second warehouse's shift is open at the same time).</summary>
    Task<IReadOnlyList<SaleListItem>> ListCompletedByWarehouseAsync(
        WarehouseId warehouseId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken);

    /// <summary>Draft invoices that have at least one item, oldest first.</summary>
    Task<IReadOnlyList<SaleListItem>> ListDraftsWithItemsAsync(CancellationToken cancellationToken);

    /// <summary>One page of the product list, with the total number of matching products.</summary>
    Task<(IReadOnlyList<SaleProductListItem> Items, int TotalCount)> BrowseProductsAsync(
        ProductListCriteria criteria,
        CancellationToken cancellationToken);

    /// <summary>The three numbers the pencil window shows next to its price field — see <see cref="LineEditInfo"/>.</summary>
    Task<LineEditInfo> ReadLineEditInfoAsync(
        WarehouseId warehouseId,
        ProductId productId,
        CustomerId? customerId,
        CancellationToken cancellationToken);

    /// <summary>
    /// The completed invoice with this number — or, when it was corrected, the
    /// correction standing in for it (the one a return may be taken against).
    /// Null when no completed invoice has the number.
    /// </summary>
    Task<SaleListItem?> FindCompletedByNumberAsync(long number, CancellationToken cancellationToken);

    /// <summary>Returns completed in one warehouse with <paramref name="fromUtc"/> ≤ completion &lt; <paramref name="toUtc"/>, newest first — what a cash shift needs to know about money that left the drawer.</summary>
    Task<IReadOnlyList<ReturnListItem>> ListReturnsByWarehouseAsync(
        WarehouseId warehouseId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken);
}
