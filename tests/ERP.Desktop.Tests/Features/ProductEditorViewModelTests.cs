using ERP.Application.Catalog;
using ERP.Application.Common;
using ERP.Domain.Catalog;
using ERP.Desktop.Tests.TestDoubles;
using ERP.Presentation.Features.Products;

namespace ERP.Desktop.Tests.Features;

public sealed class ProductEditorViewModelTests
{
    [Fact]
    public async Task SaveWithBlankNameShowsInlinePersianErrorWithoutCallingHandler()
    {
        var handler = new RecordingCreateProductHandler();
        var viewModel = new ProductEditorViewModel(handler, new StubCatalogLookupReader())
        {
            Name = " ",
        };

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal("نام کالا را وارد کنید.", viewModel.NameError);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task SaveAcceptsPersianTomanPriceAndNormalizesBarcode()
    {
        var handler = new RecordingCreateProductHandler();
        var viewModel = new ProductEditorViewModel(handler, new StubCatalogLookupReader())
        {
            Name = "برنج ایرانی",
            CategoryId = CategoryId.New(),
            BaseUnitId = UnitId.New(),
            SalePriceTomansText = "۲۴۵٬۰۰۰",
            Barcode = " 6260001000011 ",
        };

        await viewModel.SaveCommand.ExecuteAsync(null);

        var command = Assert.IsType<CreateProductCommand>(handler.LastCommand);
        Assert.Equal(2_450_000, command.SalePrice.Rials);
        Assert.Equal(["6260001000011"], command.Barcodes);
        Assert.Equal("کالا با موفقیت ساخته شد.", viewModel.StatusMessage);
    }

    [Fact]
    public async Task LoadExposesChoicesAndSelectsConfiguredDefaults()
    {
        var defaultCategoryId = CategoryId.New();
        var defaultUnitId = UnitId.New();
        var reader = new StubCatalogLookupReader(
            new CatalogLookupSnapshot(
                [new CategoryLookupItem(defaultCategoryId, "عمومی", null, 0)],
                [new UnitLookupItem(defaultUnitId, "عدد", "عدد", false)],
                []));
        var viewModel = new ProductEditorViewModel(
            new RecordingCreateProductHandler(),
            reader);

        await viewModel.LoadAsync(
            defaultCategoryId,
            defaultUnitId,
            CancellationToken.None);

        Assert.Equal(defaultCategoryId, viewModel.CategoryId);
        Assert.Equal(defaultUnitId, viewModel.BaseUnitId);
        Assert.Equal("عمومی", Assert.Single(viewModel.Categories).Name);
        Assert.Equal("عدد", Assert.Single(viewModel.Units).Name);
    }

    private sealed class RecordingCreateProductHandler : ICreateProductHandler
    {
        public int CallCount { get; private set; }

        public CreateProductCommand? LastCommand { get; private set; }

        public Task<Result<ProductId>> ExecuteAsync(
            CreateProductCommand command,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastCommand = command;
            return Task.FromResult(Result.Success(ProductId.New()));
        }
    }
}
