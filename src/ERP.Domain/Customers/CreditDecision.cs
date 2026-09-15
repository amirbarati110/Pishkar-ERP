namespace ERP.Domain.Customers;

/// <summary>
/// Outcome of the credit check before a نسیه/چک sale — source-of-truth §6.7,
/// which defines exactly three outcomes: Warning, Manager Approval, Block.
/// "Warning" is folded into <see cref="Allowed"/> here because a warning does
/// not change what the system permits; it changes what the screen says, and the
/// screen already shows the outstanding balance banner.
/// </summary>
public enum CreditDecision
{
    Allowed = 1,
    RequiresApproval = 2,
    Blocked = 3,
}
