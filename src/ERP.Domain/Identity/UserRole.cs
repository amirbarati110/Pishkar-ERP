namespace ERP.Domain.Identity;

/// <summary>
/// «کاربران، نقش‌ها و مجوزهای پایه» (milestone-1 build order, step 4) — a flat
/// two-role model on purpose, not a permission matrix: the source-of-truth's
/// own scope for this stage is "پایه" (basic). Every place that currently
/// says a rule needs "role-checking Permission" (§6.7 manager approval,
/// checklist appendix د.۲) checks against this enum.
/// </summary>
public enum UserRole
{
    Cashier = 1,
    Admin = 2,
}
