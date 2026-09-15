using ERP.Application.Catalog;
using ERP.Application.Common;
using ERP.Presentation.Features.Categories;
using ERP.Domain.Catalog;
using ERP.Desktop.Tests.TestDoubles;

namespace ERP.Desktop.Tests.Features;

public sealed class CategoriesViewModelTests
{
    [Fact]
    public async Task SaveWithBlankNameShowsInlinePersianErrorWithoutCallingHandler()
    {
        var handler = new RecordingCreateCategoryHandler();
        var viewModel = new CategoriesViewModel(handler, new StubCatalogLookupReader())
        {
            Name = "  ",
        };

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal("نام دسته‌بندی را وارد کنید.", viewModel.NameError);
        Assert.Equal(0, handler.CallCount);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task LoadExposesPersistedCategories()
    {
        var categoryId = CategoryId.New();
        var reader = new StubCatalogLookupReader(
            new CatalogLookupSnapshot(
                [new CategoryLookupItem(categoryId, "خشکبار", null, 0)],
                [],
                []));
        var viewModel = new CategoriesViewModel(
            new RecordingCreateCategoryHandler(),
            reader);

        await viewModel.LoadAsync(CancellationToken.None);

        var category = Assert.Single(viewModel.Categories);
        Assert.Equal(categoryId, category.Id);
        Assert.Equal("خشکبار", category.Name);
    }

    private sealed class RecordingCreateCategoryHandler : ICreateCategoryHandler
    {
        public int CallCount { get; private set; }

        public Task<Result<CategoryId>> ExecuteAsync(
            CreateCategoryCommand command,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(Result.Success(CategoryId.New()));
        }
    }
}
