using ERP.Application.Customers;
using ERP.Application.Sales;
using ERP.Domain.Common;
using ERP.Domain.Inventory;
using ERP.Domain.Sales;

namespace ERP.Application.Cashiering;

/// <summary>
/// Everything that moved cash through one till in a window, split the way the shift screen shows
/// it. <see cref="HandedBack"/> is returns refunded in cash plus corrections that lowered what a
/// cash invoice charged; <see cref="Receipts"/> is «دریافت از مشتری» paid in cash at this till.
/// </summary>
public sealed record ShiftCashFigures(Money Sales, Money HandedBack, Money Receipts);

/// <summary>
/// The one place a till's cash is added up — shared by the live status and the close, so the two
/// can never disagree (§5: only the Application layer combines Sales, Customers and Cashiering).
/// </summary>
internal static class ShiftCash
{
    public static async Task<ShiftCashFigures> ReadAsync(
        ISaleReadReader sales,
        ICustomerCashReceiptReader receipts,
        WarehouseId warehouseId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken)
    {
        var invoices = await sales
            .ListTillSalesByWarehouseAsync(warehouseId, fromUtc, toUtc, cancellationToken)
            .ConfigureAwait(false);
        var cashIn = invoices.Sum(invoice => Math.Max(0, invoice.CashDeltaRials));
        var correctedBack = invoices.Sum(invoice => Math.Max(0, -invoice.CashDeltaRials));

        var returns = await sales
            .ListReturnsByWarehouseAsync(warehouseId, fromUtc, toUtc, cancellationToken)
            .ConfigureAwait(false);
        var refunded = returns
            .Where(item => item.RefundMethod == PaymentMethod.Cash)
            .Sum(item => item.Refund.Rials);

        var received = await receipts
            .SumCashReceivedAsync(warehouseId, fromUtc, toUtc, cancellationToken)
            .ConfigureAwait(false);

        return new ShiftCashFigures(
            Money.FromRials(cashIn),
            Money.FromRials(refunded + correctedBack),
            received);
    }
}
