using ERP.Application.Sales;
using ERP.Application.Tests.TestDoubles;
using ERP.Domain.Catalog;
using ERP.Domain.Common;
using ERP.Domain.Inventory;
using ERP.Domain.Sales;

namespace ERP.Application.Tests.Sales;

public sealed class AddSaleLineTests
{
    [Fact]
    public async Task ExecuteFailsWhenSaleDoesNotExist()
    {
        var context = new ApplicationTestContext();
        var handler = CreateHandler(context);

        var result = await handler.ExecuteAsync(
            new AddSaleLineCommand(SaleId.New(), ProductId.New(), 1),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("sales.sale.not-found", result.Error?.Code);
    }

    [Fact]
    public async Task ExecuteFailsWhenProductDoesNotExist()
    {
        var context = new ApplicationTestContext();
        var sale = Sale.OpenDraft(WarehouseId.New(), null, context.Clock.UtcNow);
        context.Sales.Items.Add(sale);
        var handler = CreateHandler(context);

        var result = await handler.ExecuteAsync(
            new AddSaleLineCommand(sale.Id, ProductId.New(), 1),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("sales.sale.product-not-found", result.Error?.Code);
    }

    [Fact]
    public async Task ExecuteAddsTheLineAtTheProductsCurrentSalePriceAndCommits()
    {
        var context = new ApplicationTestContext();
        var sale = Sale.OpenDraft(WarehouseId.New(), null, context.Clock.UtcNow);
        context.Sales.Items.Add(sale);
        var product = Product.Create(
            "گردو ایرانی", "WLN-1", CategoryId.New(), UnitId.New(), Money.FromTomans(1_250_000));
        context.Products.Items.Add(product);
        var handler = CreateHandler(context);

        var result = await handler.ExecuteAsync(
            new AddSaleLineCommand(sale.Id, product.Id, 2),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var line = Assert.Single(sale.Lines);
        Assert.Equal(product.Id, line.ProductId);
        Assert.Equal(2, line.Quantity.Value);
        Assert.Equal(product.SalePrice, line.UnitPrice);
        Assert.Equal(1, context.UnitOfWork.CommitCount);
    }

    private static AddSaleLineHandler CreateHandler(ApplicationTestContext context)
    {
        return new AddSaleLineHandler(context.Sales, context.Products, context.UnitOfWork);
    }
}
