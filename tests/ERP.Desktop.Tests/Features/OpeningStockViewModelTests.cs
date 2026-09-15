using ERP.Application.Catalog;
using ERP.Application.Common;
using ERP.Application.Inventory;
using ERP.Domain.Catalog;
using ERP.Domain.Inventory;
using ERP.Desktop.Tests.TestDoubles;
using ERP.Presentation.Features.Inventory;

namespace ERP.Desktop.Tests.Features;

public sealed class OpeningStockViewModelTests
{
    [Fact]
    public async Task SaveWithZeroQuantityShowsInlinePersianErrorWithoutCallingHandler()
    {
        var handler = new RecordingOpeningStockHandler();
        var viewModel = CreateReadyViewModel(handler);
        viewModel.QuantityText = "۰";

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal("تعداد باید بیشتر از صفر باشد.", viewModel.QuantityError);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task SaveAcceptsPersianDecimalQuantityAndTomanCost()
    {
        var handler = new RecordingOpeningStockHandler();
        var viewModel = CreateReadyViewModel(handler);
        viewModel.QuantityText = "۱۲٫۵";
        viewModel.UnitCostTomansText = "۱۰۰٬۰۰۰";

        await viewModel.SaveCommand.ExecuteAsync(null);

        var command = Assert.IsType<ReceiveOpeningStockCommand>(handler.LastCommand);
        Assert.Equal(12.5m, command.Quantity);
        Assert.Equal(1_000_000, command.UnitCostRials);
        Assert.Equal("موجودی اولیه با موفقیت ثبت شد.", viewModel.StatusMessage);
    }

    private static OpeningStockViewModel CreateReadyViewModel(RecordingOpeningStockHandler handler)
    {
        return new OpeningStockViewModel(handler, new StubCatalogLookupReader())
        {
            ProductId = ProductId.New(),
            WarehouseId = WarehouseId.New(),
            ReceivedOn = DateOnly.FromDateTime(DateTime.UtcNow),
        };
    }

    [Fact]
    public async Task LoadExposesPersistedProducts()
    {
        var productId = ProductId.New();
        var reader = new StubCatalogLookupReader(
            new CatalogLookupSnapshot(
                [],
                [],
                [new ProductLookupItem(productId, "برنج ایرانی", "RICE-1", "6260001000011")]));
        var viewModel = new OpeningStockViewModel(
            new RecordingOpeningStockHandler(),
            reader);

        await viewModel.LoadAsync(CancellationToken.None);

        var product = Assert.Single(viewModel.Products);
        Assert.Equal(productId, product.Id);
        Assert.Equal("برنج ایرانی", product.Name);
    }

    private sealed class RecordingOpeningStockHandler : IReceiveOpeningStockHandler
    {
        public int CallCount { get; private set; }

        public ReceiveOpeningStockCommand? LastCommand { get; private set; }

        public Task<Result<bool>> ExecuteAsync(
            ReceiveOpeningStockCommand command,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastCommand = command;
            return Task.FromResult(Result.Success(true));
        }
    }
}
