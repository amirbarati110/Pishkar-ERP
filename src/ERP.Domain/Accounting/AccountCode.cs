namespace ERP.Domain.Accounting;

/// <summary>
/// «سند حسابداری حداقلی» (milestone-1 §10.5 Auto Accounting, §6.23). A fixed,
/// hardcoded chart of accounts — not the configurable one §10.6 «Accounting
/// Mapping» describes; that is full-scope accounting, explicitly out of
/// milestone 1.
/// </summary>
public enum AccountCode
{
    Cash = 1,
    CardClearing = 2,
    AccountsReceivable = 3,
    ChequesReceivable = 4,
    SalesRevenue = 5,
    VatPayable = 6,
    CostOfGoodsSold = 7,
    Inventory = 8,
    SalesReturns = 9,

    /// <summary>
    /// «سرمایه و مانده‌های افتتاحیه»: the other side of everything the business brought in when it
    /// started using the app — opening stock and customers' opening debts. Without it those
    /// balances had no entry, and each sale's cost drove «موجودی کالا» below zero (audit 1405/07/02).
    /// </summary>
    OpeningBalanceEquity = 10,
}
