using ERP.Domain.Accounting;
using ERP.Domain.Common;
using ERP.Domain.Sales;

namespace ERP.Application.Accounting;

/// <summary>
/// «سند حسابداری حداقلی» for one completed sale (or correction) — §10.5 Auto
/// Accounting: the cashier never sees or builds this, it is produced the
/// instant the sale completes. Deliberately minimal (checklist appendix ط):
/// no COGS/inventory line yet, discount and service charge are folded into
/// the one Sales Revenue line rather than broken out.
/// </summary>
public static class SaleJournalEntryFactory
{
    /// <summary>Null for a fully-discounted (zero-total) sale — no cash moved and no revenue was earned, so there is nothing to post.</summary>
    public static JournalEntry? Create(
        JournalSourceType sourceType,
        string sourceId,
        SaleTotals totals,
        PaymentMethod paymentMethod,
        DateTimeOffset postedAtUtc)
    {
        if (totals.Total.Rials == 0)
        {
            return null;
        }

        var debitAccount = paymentMethod switch
        {
            PaymentMethod.Cash => AccountCode.Cash,
            PaymentMethod.Card => AccountCode.CardClearing,
            PaymentMethod.Credit => AccountCode.AccountsReceivable,
            PaymentMethod.Cheque => AccountCode.ChequesReceivable,
            _ => throw new DomainException("این روش پرداخت برای سند حسابداری پشتیبانی نمی‌شود."),
        };

        var lines = new List<JournalLine> { JournalLine.Debited(debitAccount, totals.Total) };

        var revenue = Money.FromRials(totals.Total.Rials - totals.Tax.Rials);
        if (revenue.Rials > 0)
        {
            lines.Add(JournalLine.Credited(AccountCode.SalesRevenue, revenue));
        }

        if (totals.Tax.Rials > 0)
        {
            lines.Add(JournalLine.Credited(AccountCode.VatPayable, totals.Tax));
        }

        return JournalEntry.Create(sourceType, sourceId, postedAtUtc, lines);
    }
}
