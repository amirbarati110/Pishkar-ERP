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
}
