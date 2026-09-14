using ERP.Application.Inventory;
using ERP.Application.Tests.TestDoubles;
using ERP.Domain.Catalog;
using ERP.Domain.Inventory;

namespace ERP.Application.Tests.Inventory;

public sealed class ReceiveOpeningStockTests
{
    [Fact]
    public async Task ExecuteCreatesLedgerMovementAuditAndSingleCommit()
    {
        var context = new ApplicationTestContext();
        var handler = new ReceiveOpeningStockHandler(
            context.StockLedgers,
            context.Audit,
            context.UnitOfWork,
            context.User,
            context.Clock);
        var productId = ProductId.New();
        var warehouseId = WarehouseId.New();

        var result = await handler.ExecuteAsync(
            new ReceiveOpeningStockCommand(
                productId,
                warehouseId,
                10m,
                1_000_000,
                new DateOnly(2026, 9, 14)),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var ledger = Assert.Single(context.StockLedgers.Items);
        Assert.Equal(10m, ledger.AvailableQuantity.Value);
        Assert.Single(ledger.Movements);
        Assert.Equal("inventory.opening-stock.received", Assert.Single(context.Audit.Entries).Action);
        Assert.Equal(1, context.UnitOfWork.CommitCount);
    }
}
