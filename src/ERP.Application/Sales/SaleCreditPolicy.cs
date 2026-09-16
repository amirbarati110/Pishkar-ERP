using ERP.Application.Customers;
using ERP.Domain.Common;
using ERP.Domain.Customers;
using ERP.Domain.Sales;

namespace ERP.Application.Sales;

/// <summary>
/// Source-of-truth §6.7's credit-limit check, shared by every path that can
/// complete a sale as نسیه — a first-time invoice (<see cref="CompleteSaleHandler"/>)
/// and a صورتحساب اصلاحی (<see cref="CompleteSaleCorrectionHandler"/>). One
/// place, so the rule (and its wording) cannot drift between the two.
/// </summary>
internal static class SaleCreditPolicy
{
    public sealed record Rejection(string Code, string Message);

    /// <summary>
    /// Only نسیه is checked against the credit limit. A cheque clears the
    /// customer's account when received (see <see cref="CustomerAccount"/>);
    /// the risk it carries — bouncing — is judged by the cheque history that
    /// Treasury will track (§6.7 «Returned Cheque»), not by the open balance.
    /// </summary>
    public static async Task<Rejection?> EvaluateAsync(
        Sale sale,
        PaymentMethod method,
        decimal taxRatePercent,
        bool approveCreditOverLimit,
        ICustomerRepository customers,
        ICustomerLedgerReader customerLedger,
        CancellationToken cancellationToken)
    {
        if (method is not (PaymentMethod.Credit or PaymentMethod.Cheque))
        {
            return null;
        }

        if (sale.CustomerId is not { } customerId)
        {
            return new Rejection(
                "sales.sale.customer-required",
                "برای فروش نسیه یا چکی، اول مشتری را انتخاب کنید.");
        }

        var customer = await customers.GetByIdAsync(customerId, cancellationToken).ConfigureAwait(false);
        if (customer is null)
        {
            return new Rejection("customers.customer.not-found", "مشتری این فاکتور یافت نشد.");
        }

        if (method != PaymentMethod.Credit)
        {
            return null;
        }

        var account = await CustomerAccounts.LoadAsync(customer, customerLedger, cancellationToken).ConfigureAwait(false);
        var amount = sale.PreviewTotals(taxRatePercent).Total;

        return customer.EvaluateCredit(account.Debt, amount) switch
        {
            CreditDecision.Allowed => null,
            CreditDecision.RequiresApproval when approveCreditOverLimit => null,
            CreditDecision.RequiresApproval => new Rejection(
                "sales.credit.requires-approval",
                $"با این فاکتور، بدهی «{customer.Name}» به {FormatToman(account.Debt.Add(amount))} تومان می‌رسد " +
                $"و از سقف اعتبار ({FormatToman(customer.CreditLimit)} تومان) بیشتر می‌شود؛ تأیید مدیر لازم است."),
            _ => new Rejection(
                "sales.credit.blocked",
                customer.Status == CustomerStatus.Active
                    ? $"بدهی «{customer.Name}» با این فاکتور خیلی بیشتر از سقف اعتبار می‌شود؛ فروش نسیه ممکن نیست."
                    : $"«{customer.Name}» بایگانی شده است؛ فروش نسیه به او ممکن نیست."),
        };
    }

    // Whole Tomans for the message only; the check itself compares exact Rials.
    private static string FormatToman(Money amount) => PersianNumber.FormatGrouped(amount.Rials / 10);
}
