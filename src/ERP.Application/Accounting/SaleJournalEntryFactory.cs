using ERP.Domain.Accounting;
using ERP.Domain.Common;
using ERP.Domain.Sales;

namespace ERP.Application.Accounting;

/// <summary>
/// «سند حسابداری حداقلی» for one completed sale (or correction) — §10.5 Auto
/// Accounting: the cashier never sees or builds this, it is produced the
/// instant the sale completes. Deliberately minimal (checklist appendix ط):
/// discount and service charge are folded into the one Sales Revenue line
/// rather than broken out. When the sale consumed real FIFO stock, a second
/// balanced pair records its cost: debit cost of goods sold, credit inventory.
/// </summary>
public static class SaleJournalEntryFactory
{
    /// <summary>
    /// Null when nothing moved at all — a fully-discounted (zero-total) sale
    /// that also consumed no costed stock. A zero-total sale that still took
    /// costed goods off the shelf posts the cost pair alone: the stock is
    /// gone whatever the customer paid.
    /// </summary>
    public static JournalEntry? Create(
        JournalSourceType sourceType,
        string sourceId,
        SaleTotals totals,
        PaymentMethod paymentMethod,
        DateTimeOffset postedAtUtc,
        Money costOfGoodsSold = default)
    {
        if (totals.Total.Rials == 0 && costOfGoodsSold.Rials == 0)
        {
            return null;
        }

        var lines = new List<JournalLine>();

        if (totals.Total.Rials > 0)
        {
            var debitAccount = paymentMethod switch
            {
                PaymentMethod.Cash => AccountCode.Cash,
                PaymentMethod.Card => AccountCode.CardClearing,
                PaymentMethod.Credit => AccountCode.AccountsReceivable,
                PaymentMethod.Cheque => AccountCode.ChequesReceivable,
                _ => throw new DomainException("این روش پرداخت برای سند حسابداری پشتیبانی نمی‌شود."),
            };

            lines.Add(JournalLine.Debited(debitAccount, totals.Total));

            var revenue = Money.FromRials(totals.Total.Rials - totals.Tax.Rials);
            if (revenue.Rials > 0)
            {
                lines.Add(JournalLine.Credited(AccountCode.SalesRevenue, revenue));
            }

            if (totals.Tax.Rials > 0)
            {
                lines.Add(JournalLine.Credited(AccountCode.VatPayable, totals.Tax));
            }
        }

        if (costOfGoodsSold.Rials > 0)
        {
            lines.Add(JournalLine.Debited(AccountCode.CostOfGoodsSold, costOfGoodsSold));
            lines.Add(JournalLine.Credited(AccountCode.Inventory, costOfGoodsSold));
        }

        return JournalEntry.Create(sourceType, sourceId, postedAtUtc, lines);
    }
}
