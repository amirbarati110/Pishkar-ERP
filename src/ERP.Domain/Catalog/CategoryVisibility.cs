namespace ERP.Domain.Catalog;

public readonly record struct CategoryVisibility(
    bool IsVisibleInPos,
    bool IsVisibleOnline,
    bool IsVisibleInPurchasing)
{
    public static CategoryVisibility Everywhere => new(true, true, true);
}

