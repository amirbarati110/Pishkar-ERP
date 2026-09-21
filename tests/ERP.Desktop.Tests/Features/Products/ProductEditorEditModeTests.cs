using ERP.Application.Catalog;
using ERP.Application.Common;
using ERP.Desktop.Tests.TestDoubles;
using ERP.Domain.Catalog;
using ERP.Domain.Common;
using ERP.Presentation.Features.Products;

namespace ERP.Desktop.Tests.Features.Products;

/// <summary>The product form used from «لیست کالاها»: edit (F3), save-and-return, save-and-next.</summary>
public sealed class ProductEditorEditModeTests
{
    private static readonly CategoryId Category = CategoryId.New();
    private static readonly UnitId Unit = UnitId.New();

    private static ProductListRow ExistingRow() =>
        new(ProductId.New(), "rice-1", "برنج هاشمی", Category, "مواد غذایی", Unit, "عدد", Money.FromTomans(245_000), 4, "6260000001111");

    [Fact]
    public void BeginEditFillsTheFormFromTheRowAndSwitchesTheTitle()
    {
        var viewModel = new ProductEditorViewModel(new StubCreate(), new StubCatalogLookupReader(), new RecordingUpdate());

        viewModel.BeginEdit(ExistingRow());

        Assert.True(viewModel.IsEditing);
        Assert.Equal("ویرایش کالا", viewModel.PageTitle);
        Assert.Equal("برنج هاشمی", viewModel.Name);
        Assert.Equal("rice-1", viewModel.Sku);
        Assert.Equal("6260000001111", viewModel.Barcode);
        Assert.Equal(Category, viewModel.CategoryId);
        Assert.Equal(Unit, viewModel.BaseUnitId);
        Assert.Equal("۲۴۵,۰۰۰", viewModel.SalePriceTomansText);
    }

    [Fact]
    public async Task SavingAnEditCallsUpdateNotCreateAndAsksToReturnToTheList()
    {
        var create = new StubCreate();
        var update = new RecordingUpdate();
        var row = ExistingRow();
        var viewModel = new ProductEditorViewModel(create, new StubCatalogLookupReader(), update);
        viewModel.BeginEdit(row);
        viewModel.Name = "برنج هاشمی ممتاز";
        viewModel.SalePriceTomansText = "۲۵۰٬۰۰۰";
        var returned = 0;
        viewModel.SavedAndClosed += (_, _) => returned++;

        await viewModel.SaveAndReturnCommand.ExecuteAsync(null);

        var command = Assert.IsType<UpdateProductCommand>(update.Last);
        Assert.Equal(row.Id, command.ProductId);
        Assert.Equal("برنج هاشمی ممتاز", command.Name);
        Assert.Equal(2_500_000, command.SalePrice.Rials);
        Assert.Equal(0, create.Calls);
        Assert.Equal(1, returned);
    }

    [Fact]
    public async Task ADuplicateCodeStaysOnTheFormAndMarksTheCodeField()
    {
        var update = new RecordingUpdate { Failure = new ApplicationError("catalog.product.duplicate-sku", "این کد کالا قبلاً استفاده شده است.") };
        var viewModel = new ProductEditorViewModel(new StubCreate(), new StubCatalogLookupReader(), update);
        viewModel.BeginEdit(ExistingRow());
        var returned = 0;
        viewModel.SavedAndClosed += (_, _) => returned++;

        await viewModel.SaveAndReturnCommand.ExecuteAsync(null);

        Assert.Equal("این کد کالا قبلاً استفاده شده است.", viewModel.SkuError);
        Assert.Equal(0, returned);
    }

    [Fact]
    public async Task SaveAndNextClearsTheEntryFieldsButKeepsCategoryAndUnit()
    {
        var create = new StubCreate();
        var viewModel = new ProductEditorViewModel(create, new StubCatalogLookupReader(), new RecordingUpdate())
        {
            Name = "گردو",
            Sku = "N-1",
            Barcode = "6260000009999",
            CategoryId = Category,
            BaseUnitId = Unit,
            SalePriceTomansText = "۴۲۰٬۰۰۰",
        };

        await viewModel.SaveAndNextCommand.ExecuteAsync(null);

        Assert.Equal(1, create.Calls);
        Assert.Equal(string.Empty, viewModel.Name);
        Assert.Null(viewModel.Sku);
        Assert.Null(viewModel.Barcode);
        Assert.Equal(string.Empty, viewModel.SalePriceTomansText);
        Assert.Equal(Category, viewModel.CategoryId);
        Assert.Equal(Unit, viewModel.BaseUnitId);
        Assert.Equal("کالا با موفقیت ساخته شد.", viewModel.StatusMessage);
    }

    [Fact]
    public async Task SaveAndNextKeepsTheFormWhenTheSaveFails()
    {
        var create = new StubCreate { FailWith = new ApplicationError("catalog.product.duplicate-barcode", "این بارکد قبلاً ثبت شده است.") };
        var viewModel = new ProductEditorViewModel(create, new StubCatalogLookupReader(), new RecordingUpdate())
        {
            Name = "گردو",
            CategoryId = Category,
            BaseUnitId = Unit,
            SalePriceTomansText = "۱۰۰",
            Barcode = "6260000009999",
        };

        await viewModel.SaveAndNextCommand.ExecuteAsync(null);

        Assert.Equal("گردو", viewModel.Name);
        Assert.Equal("این بارکد قبلاً ثبت شده است.", viewModel.BarcodeError);
    }

    private sealed class StubCreate : ICreateProductHandler
    {
        public int Calls { get; private set; }

        public ApplicationError? FailWith { get; set; }

        public Task<Result<ProductId>> ExecuteAsync(CreateProductCommand command, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(FailWith is { } error
                ? Result.Failure<ProductId>(error.Code, error.Message)
                : Result.Success(ProductId.New()));
        }
    }

    private sealed class RecordingUpdate : IUpdateProductHandler
    {
        public UpdateProductCommand? Last { get; private set; }

        public ApplicationError? Failure { get; set; }

        public Task<Result<bool>> ExecuteAsync(UpdateProductCommand command, CancellationToken cancellationToken)
        {
            Last = command;
            return Task.FromResult(Failure is { } error
                ? Result.Failure<bool>(error.Code, error.Message)
                : Result.Success(true));
        }
    }
}
