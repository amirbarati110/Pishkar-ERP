using ERP.Domain.Catalog;
using ERP.Domain.Catalog.Events;
using ERP.Domain.Common;

namespace ERP.Domain.Tests.Catalog;

public sealed class CategoryTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateRejectsEmptyName(string name)
    {
        var exception = Assert.Throws<DomainException>(() => Category.Create(name, null, 0));

        Assert.Equal("نام دسته‌بندی را وارد کنید.", exception.Message);
    }

    [Fact]
    public void CreateRejectsNameLongerThanOneHundredTwentyCharacters()
    {
        var longName = new string('ک', 121);

        var exception = Assert.Throws<DomainException>(() => Category.Create(longName, null, 0));

        Assert.Equal("نام دسته‌بندی نمی‌تواند بیشتر از ۱۲۰ نویسه باشد.", exception.Message);
    }

    [Fact]
    public void CreateNormalizesWhitespaceAroundName()
    {
        var category = Category.Create("  خشکبار  ", null, 0);

        Assert.Equal("خشکبار", category.Name);
    }

    [Fact]
    public void CreateRejectsNegativeSortOrder()
    {
        var exception = Assert.Throws<DomainException>(() => Category.Create("خشکبار", null, -1));

        Assert.Equal("ترتیب نمایش نمی‌تواند منفی باشد.", exception.Message);
    }

    [Fact]
    public void MoveToRejectsSelfAsParent()
    {
        var category = Category.Create("خشکبار", null, 0);

        var exception = Assert.Throws<DomainException>(() => category.MoveTo(category.Id, 1));

        Assert.Equal("یک دسته‌بندی نمی‌تواند زیرمجموعه خودش باشد.", exception.Message);
    }

    [Fact]
    public void MoveToChangesParentAndRaisesEvent()
    {
        var category = Category.Create("گردو", null, 0);
        var newParentId = CategoryId.New();

        category.MoveTo(newParentId, 2);

        Assert.Equal(newParentId, category.ParentId);
        Assert.Equal(2, category.SortOrder);
        var moved = Assert.Single(category.DomainEvents.OfType<CategoryMoved>());
        Assert.Equal(category.Id, moved.CategoryId);
        Assert.Equal(newParentId, moved.NewParentId);
    }

    [Fact]
    public void MoveToWithInvalidSortOrderLeavesCategoryUnchanged()
    {
        var originalParentId = CategoryId.New();
        var category = Category.Create("گردو", originalParentId, 3);

        Assert.Throws<DomainException>(() => category.MoveTo(CategoryId.New(), -1));

        Assert.Equal(originalParentId, category.ParentId);
        Assert.Equal(3, category.SortOrder);
        Assert.Empty(category.DomainEvents);
    }

    [Fact]
    public void ArchiveAndRestoreChangeStatusWithoutDeletingCategory()
    {
        var category = Category.Create("تنقلات", null, 0);

        category.Archive();
        Assert.Equal(CategoryStatus.Archived, category.Status);

        category.Restore();
        Assert.Equal(CategoryStatus.Active, category.Status);
    }

    [Fact]
    public void SetVisibilityControlsEachSalesChannelIndependently()
    {
        var category = Category.Create("کالاهای ویژه", null, 0);
        var visibility = new CategoryVisibility(
            IsVisibleInPos: true,
            IsVisibleOnline: false,
            IsVisibleInPurchasing: true);

        category.SetVisibility(visibility);

        Assert.True(category.Visibility.IsVisibleInPos);
        Assert.False(category.Visibility.IsVisibleOnline);
        Assert.True(category.Visibility.IsVisibleInPurchasing);
    }
}
