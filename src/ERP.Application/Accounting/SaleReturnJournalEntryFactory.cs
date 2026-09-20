using ERP.Domain.Accounting;
using ERP.Domain.Common;
using ERP.Domain.Sales;

namespace ERP.Application.Accounting;

/// <summary>
/// The «سند حسابداری» for one return — the mirror of a sale's: revenue and
/// VAT are given back (debit) against wherever the money left (credit), and
/// goods that went back on the shelf return to inventory at their original
/// cost (debit inventory, credit cost of goods sold). Damaged and waste rows
/// add nothing beyond the refund: their cost was already expensed when they
/// were sold, and the loss stays there.
/// </summary>
public static class SaleReturnJournalEntryFactory
{
    public static JournalEntry? Create(SaleReturn saleReturn, DateTimeOffset postedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(saleReturn);

        var lines = new List<JournalLine>();

        if (saleReturn.RefundTotal.Rials > 0)
        {
            if (saleReturn.TotalNet.Rials > 0)
            {
                lines.Add(JournalLine.Debited(AccountCode.SalesReturns, saleReturn.TotalNet));
            }

            if (saleReturn.TotalTax.Rials > 0)
            {
                lines.Add(JournalLine.Debited(AccountCode.VatPayable, saleReturn.TotalTax));
            }

            var refundedFrom = saleReturn.RefundMethod switch
            {
                PaymentMethod.Cash => AccountCode.Cash,
                PaymentMethod.Card => AccountCode.CardClearing,
                PaymentMethod.Credit => AccountCode.AccountsReceivable,
                _ => throw new DomainException("این روش بازپرداخت برای سند حسابداری پشتیبانی نمی‌شود."),
            };
            lines.Add(JournalLine.Credited(refundedFrom, saleReturn.RefundTotal));
        }

        if (saleReturn.RestockCost.Rials > 0)
        {
            lines.Add(JournalLine.Debited(AccountCode.Inventory, saleReturn.RestockCost));
            lines.Add(JournalLine.Credited(AccountCode.CostOfGoodsSold, saleReturn.RestockCost));
        }

        return lines.Count == 0
            ? null
            : JournalEntry.Create(JournalSourceType.SaleReturn, saleReturn.Id.ToString(), postedAtUtc, lines);
    }
}
