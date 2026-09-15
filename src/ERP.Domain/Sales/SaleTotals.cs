using ERP.Domain.Common;

namespace ERP.Domain.Sales;

/// <summary>
/// The invoice footer, in the order the cashier reads it:
/// مبلغ کل → تخفیف → خدمات/هزینه → مالیات → قابل پرداخت.
/// </summary>
public sealed record SaleTotals(
    Money Subtotal,
    Money Discount,
    Money ServiceCharge,
    Money Tax,
    Money Total);
