namespace ERP.Domain.Sales;

/// <summary>
/// Where a returned item goes (§6.25 «Return to Stock / Damaged / Waste»).
/// Only <see cref="ToStock"/> puts anything back on the shelf; the other two
/// give the customer their money back but leave the goods' cost expensed —
/// the loss is real, so it stays in cost of goods sold.
/// </summary>
public enum ReturnDisposition
{
    ToStock = 1,
    Damaged = 2,
    Waste = 3,
}
