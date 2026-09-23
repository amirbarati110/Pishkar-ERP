using ERP.Domain.Catalog;
using ERP.Domain.Common;
using ERP.Domain.Sales;

namespace ERP.Application.Sales;

/// <summary>
/// What completing one invoice line actually cost, recorded at that moment.
/// Stock only remembers how much is left in each layer, not which layers a
/// given sale drew from, so without this a later return could not put the
/// goods back at the cost they left at.
/// </summary>
/// <param name="CostedQuantity">
/// The part of the line that came from real stock layers — smaller than the
/// line's quantity when part of it was sold ahead of receiving (no layer, so
/// no known cost).
/// </param>
public sealed record SaleLineCost(ProductId ProductId, Money TotalCost, decimal CostedQuantity);

public interface ISaleLineCostRepository
{
    Task RecordAsync(SaleId saleId, IReadOnlyCollection<SaleLineCost> costs, CancellationToken cancellationToken);

    /// <summary>Average unit cost per product for one sale; products with no recorded cost are simply absent.</summary>
    Task<IReadOnlyDictionary<ProductId, Money>> GetUnitCostsAsync(SaleId saleId, CancellationToken cancellationToken);
}

public interface IReturnNumberGenerator
{
    Task<ReturnNumber> NextAsync(CancellationToken cancellationToken);
}

public interface ISaleReturnRepository
{
    Task AddAsync(SaleReturn saleReturn, CancellationToken cancellationToken);

    /// <summary>What earlier returns already gave back, per product, for one invoice.</summary>
    Task<IReadOnlyDictionary<ProductId, PreviouslyReturned>> GetReturnedAsync(SaleId saleId, CancellationToken cancellationToken);

    /// <summary>Whether any return exists against the invoice — a returned invoice can no longer be corrected.</summary>
    Task<bool> AnyForSaleAsync(SaleId saleId, CancellationToken cancellationToken);

    /// <summary>Whether an earlier return of the invoice already gave its service charge back (it goes back at most once).</summary>
    Task<bool> HasRefundedServiceChargeAsync(SaleId saleId, CancellationToken cancellationToken);
}

/// <summary>One completed return in a list — for the shift reconciliation and the day's lists.</summary>
public sealed record ReturnListItem(
    SaleReturnId ReturnId,
    ReturnNumber Number,
    SaleNumber? OriginalSaleNumber,
    DateTimeOffset CompletedAtUtc,
    PaymentMethod RefundMethod,
    Money Refund);
