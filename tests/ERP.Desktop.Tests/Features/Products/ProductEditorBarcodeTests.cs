using ERP.Application.Catalog;
using ERP.Application.Common;
using ERP.Desktop.Tests.TestDoubles;
using ERP.Domain.Catalog;
using ERP.Domain.Common;
using ERP.Presentation.Features.Products;

namespace ERP.Desktop.Tests.Features.Products;

/// <summary>«ساخت بارکد خودکار» on the product form (checklist «ن-۳»).</summary>
public sealed class ProductEditorBarcodeTests
{
    private static readonly CategoryId Category = CategoryId.New();
    private static readonly UnitId Unit = UnitId.New();

    private static ProductListRow RowWithBarcode() =>
        new(ProductId.New(), "rice-1", "برنج هاشمی", Category, "مواد غذایی", Unit, "عدد", Money.FromTomans(245_000), 4, "6260000001111");

    private static ProductListRow RowWithoutBarcode() =>
        new(ProductId.New(), "nut-1", "گردو", Category, "مواد غذایی", Unit, "عدد", Money.FromTomans(420_000), 4, null);

    [Fact]
    public void WithNoGeneratorTheButtonIsNeverOfferedAndNothingChanges()
    {
        var viewModel = new ProductEditorViewModel(new StubCreate(), new StubCatalogLookupReader());

        Assert.False(viewModel.CanGenerateBarcode);
    }

    [Fact]
    public void ANewProductCanAlwaysGenerateABarcode()
    {
        var viewModel = new ProductEditorViewModel(new StubCreate(), new StubCatalogLookupReader(), barcodeGenerator: new StubGenerator());

        Assert.True(viewModel.IsBarcodeEditable);
        Assert.True(viewModel.CanGenerateBarcode);
    }

    [Fact]
    public void EditingAProductThatAlreadyHasABarcodeLocksItAndHidesTheButton()
    {
        var viewModel = new ProductEditorViewModel(new StubCreate(), new StubCatalogLookupReader(), new RecordingUpdate(), new StubGenerator());

        viewModel.BeginEdit(RowWithBarcode());

        Assert.False(viewModel.IsBarcodeEditable);
        Assert.False(viewModel.CanGenerateBarcode);
    }

    [Fact]
    public void EditingAProductWithNoBarcodeStillOffersTheButton()
    {
        var viewModel = new ProductEditorViewModel(new StubCreate(), new StubCatalogLookupReader(), new RecordingUpdate(), new StubGenerator());

        viewModel.BeginEdit(RowWithoutBarcode());

        Assert.True(viewModel.IsBarcodeEditable);
        Assert.True(viewModel.CanGenerateBarcode);
    }

    [Fact]
    public async Task ClickingGenerateFillsTheBoxWithoutSaving()
    {
        var create = new StubCreate();
        var generator = new StubGenerator();
        var viewModel = new ProductEditorViewModel(create, new StubCatalogLookupReader(), barcodeGenerator: generator);

        await viewModel.GenerateBarcodeCommand.ExecuteAsync(null);

        Assert.Equal(generator.NextBarcode, viewModel.Barcode);
        Assert.Equal(0, create.Calls);
    }

    [Fact]
    public async Task GeneratingAgainWhileTheBoxIsFilledDoesNothingAndWastesNoNumber()
    {
        var generator = new StubGenerator();
        var viewModel = new ProductEditorViewModel(new StubCreate(), new StubCatalogLookupReader(), barcodeGenerator: generator)
        {
            Barcode = "6260000009999",
        };

        await viewModel.GenerateBarcodeCommand.ExecuteAsync(null);

        Assert.Equal("6260000009999", viewModel.Barcode);
        Assert.Equal(0, generator.Calls);
        Assert.NotNull(viewModel.BarcodeError);
    }

    [Fact]
    public async Task SavingANewProductWithNoBarcodeMakesOneAutomatically()
    {
        var create = new StubCreate();
        var generator = new StubGenerator();
        var viewModel = new ProductEditorViewModel(create, new StubCatalogLookupReader(), barcodeGenerator: generator)
        {
            Name = "گردو",
            CategoryId = Category,
            BaseUnitId = Unit,
            SalePriceTomansText = "۴۲۰٬۰۰۰",
        };

        await viewModel.SaveAndNextCommand.ExecuteAsync(null);

        Assert.Equal(generator.NextBarcode, create.LastBarcode);
        Assert.Contains(generator.NextBarcode, viewModel.StatusMessage);
    }

    [Fact]
    public async Task SavingAProductThatAlreadyHasATypedBarcodeDoesNotGenerateOne()
    {
        var create = new StubCreate();
        var generator = new StubGenerator();
        var viewModel = new ProductEditorViewModel(create, new StubCatalogLookupReader(), barcodeGenerator: generator)
        {
            Name = "گردو",
            CategoryId = Category,
            BaseUnitId = Unit,
            SalePriceTomansText = "۴۲۰٬۰۰۰",
            Barcode = "6260000009999",
        };

        await viewModel.SaveAndNextCommand.ExecuteAsync(null);

        Assert.Equal(0, generator.Calls);
        Assert.Equal("6260000009999", create.LastBarcode);
    }

    [Fact]
    public async Task SavingAnEditOfAnOldBarcodelessProductSendsTheGeneratedCodeAsNewBarcode()
    {
        var update = new RecordingUpdate();
        var generator = new StubGenerator();
        var viewModel = new ProductEditorViewModel(new StubCreate(), new StubCatalogLookupReader(), update, generator);
        viewModel.BeginEdit(RowWithoutBarcode());

        await viewModel.SaveAndReturnCommand.ExecuteAsync(null);

        Assert.Equal(generator.NextBarcode, update.Last!.NewBarcode);
        Assert.Contains(generator.NextBarcode, viewModel.StatusMessage);
    }

    [Fact]
    public async Task SavingAnEditOfAProductThatAlreadyHasABarcodeSendsNoNewBarcode()
    {
        var update = new RecordingUpdate();
        var generator = new StubGenerator();
        var viewModel = new ProductEditorViewModel(new StubCreate(), new StubCatalogLookupReader(), update, generator);
        viewModel.BeginEdit(RowWithBarcode());

        await viewModel.SaveAndReturnCommand.ExecuteAsync(null);

        Assert.Null(update.Last!.NewBarcode);
        Assert.Equal(0, generator.Calls);
    }

    [Fact]
    public async Task ADuplicateGeneratedBarcodeIsShownUnderTheBarcodeBox()
    {
        var update = new RecordingUpdate { Failure = new ApplicationError("catalog.product.duplicate-barcode", "این بارکد قبلاً برای کالای دیگری ثبت شده است.") };
        var generator = new StubGenerator();
        var viewModel = new ProductEditorViewModel(new StubCreate(), new StubCatalogLookupReader(), update, generator);
        viewModel.BeginEdit(RowWithoutBarcode());

        await viewModel.SaveAndReturnCommand.ExecuteAsync(null);

        Assert.Equal("این بارکد قبلاً برای کالای دیگری ثبت شده است.", viewModel.BarcodeError);
    }

    private sealed class StubGenerator : IGenerateProductBarcodeHandler
    {
        public string NextBarcode { get; } = "0400000000015";

        public int Calls { get; private set; }

        public ApplicationError? FailWith { get; set; }

        public Task<Result<string>> ExecuteAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(FailWith is { } error
                ? Result.Failure<string>(error.Code, error.Message)
                : Result.Success(NextBarcode));
        }
    }

    private sealed class StubCreate : ICreateProductHandler
    {
        public int Calls { get; private set; }

        public string? LastBarcode { get; private set; }

        public ApplicationError? FailWith { get; set; }

        public Task<Result<ProductId>> ExecuteAsync(CreateProductCommand command, CancellationToken cancellationToken)
        {
            Calls++;
            LastBarcode = command.Barcodes.FirstOrDefault();
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
