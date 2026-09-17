using ERP.Domain.Accounting;

namespace ERP.Presentation.Features.Sales;

/// <summary>Persian names for the fixed «سند حسابداری حداقلی» chart of accounts (checklist appendix ط) — a cashier never sees these; only the small manager/accountant viewer does (§6.23).</summary>
public static class AccountingText
{
    public static string AccountName(AccountCode account) => account switch
    {
        AccountCode.Cash => "صندوق (نقد)",
        AccountCode.CardClearing => "کارتخوان (در راه وصول)",
        AccountCode.AccountsReceivable => "حساب‌های دریافتنی (نسیه)",
        AccountCode.ChequesReceivable => "چک‌های دریافتنی",
        AccountCode.SalesRevenue => "درآمد فروش",
        AccountCode.VatPayable => "مالیات بر ارزش‌افزوده پرداختنی",
        _ => account.ToString(),
    };
}
