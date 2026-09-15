namespace ERP.Domain.Sales;

/// <summary>
/// The settlement methods offered on the payment screen (نقدی / کارتخوان /
/// نسیه / چک). Source-of-truth §6.16 lists more (Installment, Store Credit,
/// Wallet, Combined); each of those needs a workflow that does not exist yet
/// (a wallet needs the unbuilt CRM balance, an installment needs a plan), so
/// they stay out until that workflow lands rather than being stubbed.
///
/// Note for accounting: <see cref="Credit"/> and <see cref="Cheque"/> do not
/// settle the invoice at the till — they post a receivable against the
/// customer account. That posting belongs to Phase 4 (Treasury/Accounting) and
/// is why both require a real customer, never the walk-in "فروش نقدی" account.
/// </summary>
public enum PaymentMethod
{
    Cash = 1,
    Card = 2,
    Credit = 3,
    Cheque = 4,
}
