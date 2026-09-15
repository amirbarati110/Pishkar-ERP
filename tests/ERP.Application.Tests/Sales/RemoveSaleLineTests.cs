using ERP.Application.Sales;
using ERP.Application.Tests.TestDoubles;
using ERP.Domain.Catalog;
using ERP.Domain.Common;
using ERP.Domain.Inventory;
using ERP.Domain.Sales;

namespace ERP.Application.Tests.Sales;

public sealed class RemoveSaleLineTests
{
    [Fact]
    public async Task ExecuteFailsWhenSaleDoesNotExist()
    {
        var context = new ApplicationTestContext();
        var handler = new RemoveSaleLineHandler(context.Sales, context.UnitOfWork);

        var result = await handler.ExecuteAsync(
            new RemoveSaleLineCommand(SaleId.New(), ProductId.New()),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("sales.sale.not-found", result.Error?.Code);
    }

    [Fact]
    public async Task ExecuteFailsWithAFriendlyMessageWhenProductIsNotInTheCart()
    {
        var context = new ApplicationTestContext();
        var sale = Sale.OpenDraft(WarehouseId.New(), null, context.Clock.UtcNow);
        context.Sales.Items.Add(sale);
        var handler = new RemoveSaleLineHandler(context.Sales, context.UnitOfWork);

        var result = await handler.ExecuteAsync(
            new RemoveSaleLineCommand(sale.Id, ProductId.New()),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("این کالا در سبد فاکتور نیست.", result.Error?.Message);
        Assert.Equal(0, context.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task ExecuteRemovesTheLineAndCommits()
    {
        var context = new ApplicationTestContext();
        var sale = Sale.OpenDraft(WarehouseId.New(), null, context.Clock.UtcNow);
        var productId = ProductId.New();
        sale.AddOrIncreaseLine(productId, Quantity.Create(1), Money.FromTomans(10_000));
        context.Sales.Items.Add(sale);
        var handler = new RemoveSaleLineHandler(context.Sales, context.UnitOfWork);

        var result = await handler.ExecuteAsync(
            new RemoveSaleLineCommand(sale.Id, productId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(sale.Lines);
        Assert.Equal(1, context.UnitOfWork.CommitCount);
    }
}
