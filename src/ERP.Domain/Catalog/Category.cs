using ERP.Domain.Catalog.Events;
using ERP.Domain.Common;

namespace ERP.Domain.Catalog;

public sealed class Category : Entity<CategoryId>
{
    private const int MaximumNameLength = 120;

    private Category(
        CategoryId id,
        string name,
        CategoryId? parentId,
        int sortOrder)
        : base(id)
    {
        Name = name;
        ParentId = parentId;
        SortOrder = sortOrder;
        Status = CategoryStatus.Active;
        Visibility = CategoryVisibility.Everywhere;
    }

    public string Name { get; private set; }

    public CategoryId? ParentId { get; private set; }

    public int SortOrder { get; private set; }

    public CategoryStatus Status { get; private set; }

    public CategoryVisibility Visibility { get; private set; }

    public static Category Create(string name, CategoryId? parentId, int sortOrder)
    {
        return new Category(
            CategoryId.New(),
            NormalizeName(name),
            parentId,
            ValidateSortOrder(sortOrder));
    }

    internal static Category Rehydrate(
        CategoryId id,
        string name,
        CategoryId? parentId,
        int sortOrder,
        CategoryStatus status,
        CategoryVisibility visibility)
    {
        var category = new Category(
            id,
            NormalizeName(name),
            parentId,
            ValidateSortOrder(sortOrder));
        category.Status = status;
        category.Visibility = visibility;

        return category;
    }

    public void Rename(string name)
    {
        Name = NormalizeName(name);
    }

    public void MoveTo(CategoryId? newParentId, int newSortOrder)
    {
        if (newParentId == Id)
        {
            throw new DomainException("یک دسته‌بندی نمی‌تواند زیرمجموعه خودش باشد.");
        }

        var validatedSortOrder = ValidateSortOrder(newSortOrder);
        var previousParentId = ParentId;
        ParentId = newParentId;
        SortOrder = validatedSortOrder;

        Raise(new CategoryMoved(Id, previousParentId, newParentId, DateTimeOffset.UtcNow));
    }

    public void Archive()
    {
        Status = CategoryStatus.Archived;
    }

    public void Restore()
    {
        Status = CategoryStatus.Active;
    }

    public void SetVisibility(CategoryVisibility visibility)
    {
        Visibility = visibility;
    }

    private static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("نام دسته‌بندی را وارد کنید.");
        }

        var normalized = name.Trim();

        if (normalized.Length > MaximumNameLength)
        {
            throw new DomainException("نام دسته‌بندی نمی‌تواند بیشتر از ۱۲۰ نویسه باشد.");
        }

        return normalized;
    }

    private static int ValidateSortOrder(int sortOrder)
    {
        if (sortOrder < 0)
        {
            throw new DomainException("ترتیب نمایش نمی‌تواند منفی باشد.");
        }

        return sortOrder;
    }
}
