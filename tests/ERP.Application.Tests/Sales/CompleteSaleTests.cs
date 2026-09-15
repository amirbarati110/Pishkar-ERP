using ERP.Application.Sales;
using ERP.Application.Tests.TestDoubles;
using ERP.Domain.Catalog;
using ERP.Domain.Common;
using ERP.Domain.Inventory;
using ERP.Domain.Sales;

namespace ERP.Application.Tests.Sales;

public sealed class CompleteSaleTests
{
    [Fact]
    public async Task ExecuteFailsWhenSaleDoesNotExist()
    {
        var context = new ApplicationTestContext();
        var handler = CreateHandler(context);

        var result = await handler.ExecuteAsync(
            new CompleteSaleCommand(SaleId.New(), PaymentMethod.Cash, 0, 9),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("sales.sale.not-found", result.Error?.Code);
    }

    [Fact]
    public async Task ExecutePullsFifoStockComputesTotalsWritesAuditAndCommits()
    {
        var context = new ApplicationTestContext();
        var warehouseId = WarehouseId.New();
        var productId = ProductId.New();

        var ledger = StockLedger.Empty(productId, warehouseId);
        ledger.ReceiveOpeningStock(Quantity.Create(10), Money.FromTomans(50_000), DateOnly.FromDateTime(context.Clock.UtcNow.Date));
        context.StockLedgers.Items.Add(ledger);

        var sale = Sale.OpenDraft(warehouseId, null, context.Clock.UtcNow);
        sale.AddOrIncreaseLine(productId, Quantity.Create(4), Money.FromTomans(100_000));
        context.Sales.Items.Add(sale);

        var handler = CreateHandler(context);

        var result = await handler.ExecuteAsync(
            new CompleteSaleCommand(sale.Id, PaymentMethod.Card, DiscountRials: 0, TaxRatePercent: 9),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(400_000, result.Value!.Subtotal.ToTomansExact());
        Assert.Equal(436_000, result.Value.Total.ToTomansExact());
        Assert.Equal(SaleStatus.Completed, sale.Status);
        Assert.Equal(6, ledger.AvailableQuantity.Value);
        Assert.Single(context.Audit.Entries);
        Assert.Equal("sales.sale.completed", context.Audit.Entries[0].Action);
        Assert.Equal(1, context.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task ExecuteAllowsNegativeStockWhenExplicitlyOptedInAndTracksTheShortfall()
    {
        // "بار اومده، فروش رفته، فاکتور خرید هنوز ثبت نشده": 2 on record, 5 sold
        // with the override on — must succeed and leave a visible shortfall.
        var context = new ApplicationTestContext();
        var warehouseId = WarehouseId.New();
        var productId = ProductId.New();

        var ledger = StockLedger.Empty(productId, warehouseId);
        ledger.ReceiveOpeningStock(Quantity.Create(2), Money.FromTomans(50_000), DateOnly.FromDateTime(context.Clock.UtcNow.Date));
        context.StockLedgers.Items.Add(ledger);

        var sale = Sale.OpenDraft(warehouseId, null, context.Clock.UtcNow);
        sale.AddOrIncreaseLine(productId, Quantity.Create(5), Money.FromTomans(100_000));
        context.Sales.Items.Add(sale);

        var handler = CreateHandler(context);

        var result = await handler.ExecuteAsync(
            new CompleteSaleCommand(sale.Id, PaymentMethod.Cash, 0, 9, AllowNegativeStock: true),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SaleStatus.Completed, sale.Status);
        Assert.Equal(-3m, ledger.AvailableQuantity.Value);
        Assert.True(ledger.AvailableQuantity.IsNegative);
        Assert.Equal(1, context.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task ExecuteFailsWithoutCommittingWhenStockIsInsufficient()
    {
        var context = new ApplicationTestContext();
        var warehouseId = WarehouseId.New();
        var productId = ProductId.New();

        var ledger = StockLedger.Empty(productId, warehouseId);
        ledger.ReceiveOpeningStock(Quantity.Create(1), Money.FromTomans(50_000), DateOnly.FromDateTime(context.Clock.UtcNow.Date));
        context.StockLedgers.Items.Add(ledger);

        var sale = Sale.OpenDraft(warehouseId, null, context.Clock.UtcNow);
        sale.AddOrIncreaseLine(productId, Quantity.Create(5), Money.FromTomans(100_000));
        context.Sales.Items.Add(sale);

        var handler = CreateHandler(context);

        var result = await handler.ExecuteAsync(
            new CompleteSaleCommand(sale.Id, PaymentMethod.Cash, DiscountRials: 0, TaxRatePercent: 9),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("sales.sale.invalid", result.Error?.Code);
        Assert.Equal(SaleStatus.Draft, sale.Status);
        Assert.Empty(context.Audit.Entries);
        Assert.Equal(0, context.UnitOfWork.CommitCount);
    }

    private static CompleteSaleHandler CreateHandler(ApplicationTestContext context)
    {
        return new CompleteSaleHandler(
            context.Sales,
            context.StockLedgers,
            context.Audit,
            context.UnitOfWork,
            context.User,
            context.Clock);
    }
}
