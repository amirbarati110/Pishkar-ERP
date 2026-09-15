using ERP.Application.Sales;
using ERP.Application.Tests.TestDoubles;
using ERP.Domain.Inventory;

namespace ERP.Application.Tests.Sales;

public sealed class StartSaleTests
{
    [Fact]
    public async Task ExecuteOpensADraftSaleAndCommits()
    {
        var context = new ApplicationTestContext();
        var handler = new StartSaleHandler(context.Sales, context.UnitOfWork, context.Clock);
        var warehouseId = WarehouseId.New();

        var result = await handler.ExecuteAsync(
            new StartSaleCommand(warehouseId, CustomerId: null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var sale = Assert.Single(context.Sales.Items);
        Assert.Equal(result.Value, sale.Id);
        Assert.Equal(warehouseId, sale.WarehouseId);
        Assert.Equal(1, context.UnitOfWork.CommitCount);
    }
}
