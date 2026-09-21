using ERP.Application.Inventory;
using ERP.Domain.Catalog;
using ERP.Domain.Common;
using ERP.Domain.Inventory;

namespace ERP.Application.Tests.Inventory;

public sealed class GetStockCardTests
{
    private static readonly DateTimeOffset Day1 = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);

    private static StockCardMovement Move(int day, StockMovementType type, decimal quantity, StockCardDocumentKind kind = StockCardDocumentKind.None, long? number = null, long? valueRials = null) =>
        new(Day1.AddDays(day), type, quantity, kind, number, null, valueRials is { } rials ? Money.FromRials(rials) : null);

    private static GetStockCardHandler Handler(params StockCardMovement[] movements) =>
        new(new FakeReader(new StockCardSource("برنج", "RICE", "عدد", movements)));

    [Fact]
    public async Task BalanceRunsAfterEveryMovementAndTotalsAddUp()
    {
        var page = await Handler(
                Move(0, StockMovementType.OpeningBalance, 10),
                Move(1, StockMovementType.Sale, 3, StockCardDocumentKind.Sale, 1258),
                Move(2, StockMovementType.Return, 1, StockCardDocumentKind.Return, 1),
                Move(3, StockMovementType.AdjustmentDecrease, 2))
            .ExecuteAsync(new StockCardQuery(ProductId.New()), CancellationToken.None);

        Assert.NotNull(page);
        Assert.Equal([10m, 7m, 8m, 6m], page.Entries.Select(entry => entry.Balance));
        Assert.Equal(11m, page.TotalIn);
        Assert.Equal(5m, page.TotalOut);
        Assert.Equal(6m, page.ClosingBalance);
        Assert.Equal(0m, page.OpeningBalance);
        Assert.Equal(1258, page.Entries[1].DocumentNumber);
        Assert.Equal(3m, page.Entries[1].Out);
        Assert.Equal(0m, page.Entries[1].In);
    }

    [Fact]
    public async Task TheRunningValueFollowsTheCostOfEveryMovement()
    {
        var page = await Handler(
                Move(0, StockMovementType.OpeningBalance, 10, valueRials: 20_000_000),
                Move(1, StockMovementType.Sale, 3, StockCardDocumentKind.Sale, 1258, valueRials: 6_000_000),
                Move(2, StockMovementType.Return, 1, StockCardDocumentKind.Return, 1, valueRials: 2_000_000))
            .ExecuteAsync(new StockCardQuery(ProductId.New()), CancellationToken.None);

        Assert.Equal([20_000_000L, 14_000_000L, 16_000_000L], page!.Entries.Select(entry => entry.BalanceValue!.Value.Rials));
        Assert.Equal(16_000_000, page.ClosingValue!.Value.Rials);
        Assert.Equal(22_000_000, page.TotalInValue.Rials);
        Assert.Equal(6_000_000, page.TotalOutValue.Rials);
        Assert.Equal(0, page.UnvaluedMovements);
    }

    [Fact]
    public async Task AMovementWithoutARecordedCostMakesTheRunningValueUnknownFromThenOnNeverGuessed()
    {
        var page = await Handler(
                Move(0, StockMovementType.OpeningBalance, 10, valueRials: 20_000_000),
                Move(1, StockMovementType.Sale, 3, StockCardDocumentKind.Sale, 3),          // an old sale: cost never recorded
                Move(2, StockMovementType.Sale, 1, StockCardDocumentKind.Sale, 5, valueRials: 2_000_000))
            .ExecuteAsync(new StockCardQuery(ProductId.New()), CancellationToken.None);

        Assert.Equal(20_000_000, page!.Entries[0].BalanceValue!.Value.Rials);
        Assert.Null(page.Entries[1].Value);
        Assert.Null(page.Entries[1].BalanceValue);
        Assert.Null(page.Entries[2].BalanceValue);            // still unknown after a valued movement
        Assert.Null(page.ClosingValue);
        Assert.Equal(1, page.UnvaluedMovements);
        Assert.Equal(2_000_000, page.TotalOutValue.Rials);     // only what is known is added up
    }

    [Fact]
    public async Task APeriodStartingInTheMiddleStillShowsTheTrueStock()
    {
        var page = await Handler(
                Move(0, StockMovementType.OpeningBalance, 10),
                Move(1, StockMovementType.Sale, 3),
                Move(5, StockMovementType.Sale, 2),
                Move(9, StockMovementType.Receipt, 20))
            .ExecuteAsync(new StockCardQuery(ProductId.New(), Day1.AddDays(4), Day1.AddDays(8)), CancellationToken.None);

        Assert.NotNull(page);
        Assert.Equal(7m, page.OpeningBalance);              // 10 − 3 before the period
        var entry = Assert.Single(page.Entries);            // only the sale on day 5
        Assert.Equal(5m, entry.Balance);
        Assert.Equal(5m, page.ClosingBalance);
        Assert.Equal(2m, page.TotalOut);
        Assert.Equal(0m, page.TotalIn);
    }

    [Fact]
    public async Task AnEmptyPeriodKeepsTheOpeningBalanceAsTheClosingOne()
    {
        var page = await Handler(Move(0, StockMovementType.OpeningBalance, 10))
            .ExecuteAsync(new StockCardQuery(ProductId.New(), Day1.AddDays(3)), CancellationToken.None);

        Assert.NotNull(page);
        Assert.Empty(page.Entries);
        Assert.Equal(10m, page.OpeningBalance);
        Assert.Equal(10m, page.ClosingBalance);
    }

    [Fact]
    public async Task SellingBeyondStockShowsANegativeBalanceRatherThanHidingIt()
    {
        var page = await Handler(
                Move(0, StockMovementType.OpeningBalance, 2),
                Move(1, StockMovementType.Sale, 2),
                Move(1, StockMovementType.BackorderSale, 3))
            .ExecuteAsync(new StockCardQuery(ProductId.New()), CancellationToken.None);

        Assert.Equal(-3m, page!.ClosingBalance);
    }

    [Fact]
    public async Task AnUnknownProductGivesNull()
    {
        var handler = new GetStockCardHandler(new FakeReader(null));

        Assert.Null(await handler.ExecuteAsync(new StockCardQuery(ProductId.New()), CancellationToken.None));
    }

    private sealed class FakeReader(StockCardSource? source) : IStockCardReader
    {
        public Task<StockCardSource?> ReadAsync(ProductId productId, CancellationToken cancellationToken) => Task.FromResult(source);
    }
}
