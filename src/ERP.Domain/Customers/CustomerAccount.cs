using ERP.Domain.Common;

namespace ERP.Domain.Customers;

/// <summary>A نسیه invoice as the customer account sees it.</summary>
public sealed record CreditInvoice(long Number, DateTimeOffset IssuedAtUtc, Money Amount);

/// <summary>
/// The customer's running account (حساب مشتری), computed from what the system
/// has recorded — the numbers the sales screen shows in its customer banner:
/// «مانده قبلی ۱٬۸۵۰٬۰۰۰ تومان بدهکار • ۲ فاکتور باز» (source-of-truth §6.6).
///
/// <para><b>What counts:</b> the opening balance, every نسیه (credit) invoice,
/// minus every payment received. A <b>cheque</b> sale is deliberately not a debt
/// here: in Iranian bookkeeping a received cheque credits the customer's
/// account and the claim moves to «چک‌های دریافتنی»; the debt only returns if
/// the cheque bounces. Tracking cheques until they clear belongs to
/// Treasury (chapter 10.3), which will re-debit the customer on a bounce.</para>
///
/// <para><b>Open invoices:</b> payments are applied oldest-first — the opening
/// balance, then invoices in the order they were issued — which is how a
/// shopkeeper reads an account book without explicit settlement. An invoice
/// is open while any of it is still unpaid.</para>
/// </summary>
public sealed class CustomerAccount
{
    private CustomerAccount(Money debt, Money advance, IReadOnlyList<CreditInvoice> openInvoices)
    {
        Debt = debt;
        Advance = advance;
        OpenInvoices = openInvoices;
    }

    /// <summary>What the customer owes (بدهکار). Zero if nothing is owed.</summary>
    public Money Debt { get; }

    /// <summary>What the customer has paid ahead (بستانکار). Zero unless they overpaid.</summary>
    public Money Advance { get; }

    /// <summary>Unpaid or partly paid نسیه invoices, oldest first.</summary>
    public IReadOnlyList<CreditInvoice> OpenInvoices { get; }

    public static CustomerAccount Calculate(
        Money openingBalance,
        IEnumerable<CreditInvoice> creditInvoices,
        IEnumerable<Money> payments)
    {
        ArgumentNullException.ThrowIfNull(creditInvoices);
        ArgumentNullException.ThrowIfNull(payments);

        var unapplied = payments.Aggregate(0L, (sum, payment) => checked(sum + payment.Rials));

        var openingShortfall = openingBalance.Rials - unapplied;
        unapplied = Math.Max(0, -openingShortfall);
        var debt = Math.Max(0, openingShortfall);

        var open = new List<CreditInvoice>();
        foreach (var invoice in creditInvoices.OrderBy(item => item.IssuedAtUtc).ThenBy(item => item.Number))
        {
            var covered = Math.Min(unapplied, invoice.Amount.Rials);
            unapplied -= covered;

            var remaining = invoice.Amount.Rials - covered;
            if (remaining > 0)
            {
                open.Add(invoice);
                debt = checked(debt + remaining);
            }
        }

        return new CustomerAccount(Money.FromRials(debt), Money.FromRials(unapplied), open);
    }
}
