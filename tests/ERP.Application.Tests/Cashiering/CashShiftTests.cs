using ERP.Application.Cashiering;
using ERP.Application.Sales;
using ERP.Application.Tests.TestDoubles;
using ERP.Domain.Catalog;
using ERP.Domain.Common;
using ERP.Domain.Customers;
using ERP.Domain.Inventory;
using ERP.Domain.Sales;

namespace ERP.Application.Tests.Cashiering;

public sealed class OpenCashShiftTests
{
    [Fact]
    public async Task OpensAShiftWhenNoneIsOpenForTheWarehouse()
    {
        var context = new ApplicationTestContext();
        var warehouseId = WarehouseId.New();

        var result = await Handler(context).ExecuteAsync(
            new OpenCashShiftCommand(warehouseId, 200_000), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var shift = Assert.Single(context.CashShifts.Items);
        Assert.Equal(2_000_000, shift.OpeningCash.Rials);
        Assert.Equal(1, context.UnitOfWork.CommitCount);
        Assert.Contains(context.Audit.Entries, entry => entry.Action == "cashiering.shift.opened");
    }

    [Fact]
    public async Task RefusesASecondShiftWhileOneIsAlreadyOpen()
    {
        var context = new ApplicationTestContext();
        var warehouseId = WarehouseId.New();
        await Handler(context).ExecuteAsync(new OpenCashShiftCommand(warehouseId, 200_000), CancellationToken.None);

        var second = await Handler(context).ExecuteAsync(
            new OpenCashShiftCommand(warehouseId, 100_000), CancellationToken.None);

        Assert.False(second.IsSuccess);
        Assert.Equal("cashiering.shift.already-open", second.Error?.Code);
        Assert.Single(context.CashShifts.Items);
    }

    [Fact]
    public async Task ADifferentWarehouseCanStillOpenItsOwnShift()
    {
        var context = new ApplicationTestContext();
        await Handler(context).ExecuteAsync(new OpenCashShiftCommand(WarehouseId.New(), 200_000), CancellationToken.None);

        var second = await Handler(context).ExecuteAsync(
            new OpenCashShiftCommand(WarehouseId.New(), 100_000), CancellationToken.None);

        Assert.True(second.IsSuccess);
        Assert.Equal(2, context.CashShifts.Items.Count);
    }

    private static OpenCashShiftHandler Handler(ApplicationTestContext context) => new(
        context.CashShifts, context.Audit, context.UnitOfWork, context.User, context.Clock);
}

public sealed class CloseCashShiftTests
{
    [Fact]
    public async Task ClosingWithNoDiscrepancyReportsZeroVariance()
    {
        var context = new ApplicationTestContext();
        var warehouseId = WarehouseId.New();
        var reader = new FakeSaleReadReader();
        var opened = await OpenHandler(context).ExecuteAsync(new OpenCashShiftCommand(warehouseId, 200_000), CancellationToken.None);
        reader.Completed.Add(Item(PaymentMethod.Cash, 450_000));
        reader.Completed.Add(Item(PaymentMethod.Card, 999_000)); // نباید در جمع نقدی حساب شود

        var result = await CloseHandler(context, reader).ExecuteAsync(
            new CloseCashShiftCommand(opened.Value, 650_000, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(4_500_000, result.Value!.CashSalesDuringShift.Rials);
        Assert.Equal(6_500_000, result.Value.ExpectedCash.Rials);
        Assert.Equal(0, result.Value.VarianceRials);
        Assert.Contains(context.Audit.Entries, entry => entry.Action == "cashiering.shift.closed");
    }

    [Fact]
    public async Task ClosingShortReportsANegativeVarianceAndKeepsTheNote()
    {
        var context = new ApplicationTestContext();
        var warehouseId = WarehouseId.New();
        var reader = new FakeSaleReadReader();
        var opened = await OpenHandler(context).ExecuteAsync(new OpenCashShiftCommand(warehouseId, 200_000), CancellationToken.None);
        reader.Completed.Add(Item(PaymentMethod.Cash, 450_000));

        var result = await CloseHandler(context, reader).ExecuteAsync(
            new CloseCashShiftCommand(opened.Value, 600_000, "۵۰ تومان کم بود"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(-500_000, result.Value!.VarianceRials);
        var shift = context.CashShifts.Items.Single();
        Assert.Equal("۵۰ تومان کم بود", shift.Note);
    }

    [Fact]
    public async Task ClosingAShiftThatDoesNotExistFails()
    {
        var context = new ApplicationTestContext();
        var reader = new FakeSaleReadReader();

        var result = await CloseHandler(context, reader).ExecuteAsync(
            new CloseCashShiftCommand(Domain.Cashiering.CashShiftId.New(), 100_000, null), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("cashiering.shift.not-found", result.Error?.Code);
    }

    [Fact]
    public async Task ClosingAnAlreadyClosedShiftFails()
    {
        var context = new ApplicationTestContext();
        var warehouseId = WarehouseId.New();
        var reader = new FakeSaleReadReader();
        var opened = await OpenHandler(context).ExecuteAsync(new OpenCashShiftCommand(warehouseId, 200_000), CancellationToken.None);
        await CloseHandler(context, reader).ExecuteAsync(new CloseCashShiftCommand(opened.Value, 200_000, null), CancellationToken.None);

        var second = await CloseHandler(context, reader).ExecuteAsync(
            new CloseCashShiftCommand(opened.Value, 200_000, null), CancellationToken.None);

        Assert.False(second.IsSuccess);
        Assert.Equal("cashiering.shift.invalid", second.Error?.Code);
    }

    private static OpenCashShiftHandler OpenHandler(ApplicationTestContext context) => new(
        context.CashShifts, context.Audit, context.UnitOfWork, context.User, context.Clock);

    private static CloseCashShiftHandler CloseHandler(ApplicationTestContext context, FakeSaleReadReader reader) => new(
        context.CashShifts, reader, context.Audit, context.UnitOfWork, context.User, context.Clock);

    private static SaleListItem Item(PaymentMethod method, long tomans) => new(
        SaleId.New(), SaleNumber.From(1), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, null, 1, Money.FromTomans(tomans), method);
}

public sealed class GetCashShiftStatusTests
{
    [Fact]
    public async Task ReportsClosedWhenNoShiftIsOpen()
    {
        var context = new ApplicationTestContext();
        var reader = new FakeSaleReadReader();

        var status = await new GetCashShiftStatusHandler(context.CashShifts, reader, context.Clock)
            .ExecuteAsync(new GetCashShiftStatusQuery(WarehouseId.New()), CancellationToken.None);

        Assert.False(status.IsOpen);
        Assert.Null(status.OpeningCash);
    }

    [Fact]
    public async Task ReportsLiveExpectedCashWhileTheShiftIsStillOpen()
    {
        var context = new ApplicationTestContext();
        var warehouseId = WarehouseId.New();
        var reader = new FakeSaleReadReader();
        await new OpenCashShiftHandler(context.CashShifts, context.Audit, context.UnitOfWork, context.User, context.Clock)
            .ExecuteAsync(new OpenCashShiftCommand(warehouseId, 200_000), CancellationToken.None);
        reader.Completed.Add(new SaleListItem(
            SaleId.New(), SaleNumber.From(1), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, null, 1, Money.FromTomans(300_000), PaymentMethod.Cash));

        var status = await new GetCashShiftStatusHandler(context.CashShifts, reader, context.Clock)
            .ExecuteAsync(new GetCashShiftStatusQuery(warehouseId), CancellationToken.None);

        Assert.True(status.IsOpen);
        Assert.Equal(2_000_000, status.OpeningCash!.Value.Rials);
        Assert.Equal(3_000_000, status.CashSalesSoFar!.Value.Rials);
        Assert.Equal(5_000_000, status.ExpectedCashSoFar!.Value.Rials);
    }
}

internal sealed class FakeSaleReadReader : ISaleReadReader
{
    public List<SaleListItem> Completed { get; } = [];

    public Task<IReadOnlyDictionary<ProductId, SaleProductInfo>> ReadProductsAsync(
        WarehouseId warehouseId, IReadOnlyCollection<ProductId> productIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<ProductId, SaleProductInfo>>(new Dictionary<ProductId, SaleProductInfo>());

    public Task<IReadOnlyList<SaleListItem>> ListCompletedAsync(
        DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SaleListItem>>(Completed);

    public Task<IReadOnlyList<SaleListItem>> ListCompletedByWarehouseAsync(
        WarehouseId warehouseId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SaleListItem>>(Completed);

    public Task<IReadOnlyList<SaleListItem>> ListDraftsWithItemsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SaleListItem>>([]);

    public Task<(IReadOnlyList<SaleProductListItem> Items, int TotalCount)> BrowseProductsAsync(
        ProductListCriteria criteria, CancellationToken cancellationToken) =>
        Task.FromResult<(IReadOnlyList<SaleProductListItem>, int)>(([], 0));

    public Task<LineEditInfo> ReadLineEditInfoAsync(
        WarehouseId warehouseId, ProductId productId, CustomerId? customerId, CancellationToken cancellationToken) =>
        Task.FromResult(new LineEditInfo(null, null, null));
}
