namespace ERP.Persistence.Sales;

/// <summary>
/// The one definition of «what a return refunded», for every query that adds returns up (the
/// shift's cash refunds, the customer's balance, the customer list). A return refunds its goods
/// lines and, when the cashier chose so, the invoice's service charge — which has no line, so a
/// query that only summed SALE_RETURN_LINE would miss it (and miss a service-only return).
/// </summary>
internal static class SaleReturnSql
{
    /// <summary>The refund of the SALE_RETURN row aliased <c>R</c>.</summary>
    public const string RefundOfR =
        "(COALESCE((SELECT SUM(RL.NET_RIALS + RL.TAX_RIALS) FROM SALE_RETURN_LINE RL WHERE RL.RETURN_ID = R.ID), 0)"
        + " + R.SERVICE_NET_RIALS + R.SERVICE_TAX_RIALS)";
}
