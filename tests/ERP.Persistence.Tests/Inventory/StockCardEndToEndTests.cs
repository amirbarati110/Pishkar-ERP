using ERP.Application.Cashiering;
using ERP.Application.Catalog;
using ERP.Application.Common;
using ERP.Application.Inventory;
using ERP.Application.Sales;
using ERP.Domain.Common;
using ERP.Domain.Inventory;
using ERP.Domain.Sales;
using ERP.Persistence.Cashiering;
using ERP.Persistence.Database;
using ERP.Persistence.Inventory;
using ERP.Persistence.Sales;
using ERP.Persistence.Services;
using ERP.Persistence.Tests.TestSupport;

namespace ERP.Persistence.Tests.Inventory;

/// <summary>
/// «کاردکس کالا» on a real Firebird: opening stock → a sale → a partial return → each row names
/// the invoice/return number it came from, and the running balance matches the real stock.
/// </summary>
public sealed class StockCardEndToEndTests
{
    [Fact]
    public async Task OpeningSaleAndReturnAppearInOrderWithDocumentNumbersAndTheTrueBalance()
    {
        await using var database = FirebirdTestDatabase.Create();
        var factory = new FirebirdConnectionFactory(database.Options);
        var defaults = await new FirebirdDatabaseBootstrapper(factory).InitializeAsync(CancellationToken.None);
        var userContext = new TestUserContext(Guid.NewGuid());
        var clock = new TestClock { UtcNow = new DateTimeOffset(2026, 9, 18, 7, 0, 0, TimeSpan.Zero) };
        var setup = new FirebirdRetailSetupService(factory, userContext, clock);
        var sales = new FirebirdSalesService(factory, userContext, clock);
        var cashiering = new FirebirdCashieringService(factory, userContext, clock);
        var card = new GetStockCardHandler(new FirebirdStockCardReader(factory));

        var category = (await setup.ExecuteAsync(new CreateCategoryCommand("مواد غذایی", null, 1), CancellationToken.None)).Value;
        var product = (await setup.ExecuteAsync(
            new CreateProductCommand("برنج ایرانی", "RICE-KC", category, defaults.EachUnitId, Money.FromTomans(245_000), []),
            CancellationToken.None)).Value;
        await setup.ExecuteAsync(
            new ReceiveOpeningStockCommand(product, defaults.MainWarehouseId, 10, 2_000_000, new DateOnly(2026, 9, 1)),
            CancellationToken.None);
        await cashiering.ExecuteAsync(new OpenCashShiftCommand(defaults.MainWarehouseId, 100_000), CancellationToken.None);

        clock.UtcNow = clock.UtcNow.AddMinutes(5);
        var started = (await sales.ExecuteAsync(new StartSaleCommand(defaults.MainWarehouseId), CancellationToken.None)).Value;
        await sales.ExecuteAsync(new AddSaleLineCommand(started, product, 3), CancellationToken.None);
        var completed = await sales.ExecuteAsync(
            new CompleteSaleCommand(started, PaymentMethod.Cash, DiscountRials: 0, TaxRatePercent: 0), CancellationToken.None);
        Assert.True(completed.IsSuccess);

        clock.UtcNow = clock.UtcNow.AddMinutes(5);
        var returned = await sales.ExecuteAsync(
            new CompleteSaleReturnCommand(started, [new ReturnLineRequest(product, 1, ReturnDisposition.ToStock)], PaymentMethod.Cash, "اندازه نبود"),
            CancellationToken.None);
        Assert.True(returned.IsSuccess, returned.Error?.Message);

        var page = await card.ExecuteAsync(new StockCardQuery(product), CancellationToken.None);

        Assert.NotNull(page);
        Assert.Equal("برنج ایرانی", page.ProductName);
        Assert.Equal("RICE-KC", page.Sku);
        Assert.Equal(3, page.Entries.Count);

        var opening = page.Entries[0];
        Assert.Equal(StockMovementType.OpeningBalance, opening.Type);
        Assert.Equal(10m, opening.In);
        Assert.Equal(StockCardDocumentKind.None, opening.DocumentKind);

        var sale = page.Entries[1];
        Assert.Equal(StockMovementType.Sale, sale.Type);
        Assert.Equal(3m, sale.Out);
        Assert.Equal(StockCardDocumentKind.Sale, sale.DocumentKind);
        Assert.Equal(completed.Value!.Number.Value, sale.DocumentNumber);
        Assert.Equal(completed.Value.Number.Value, sale.RelatedSaleNumber);   // a click opens this invoice
        Assert.Null(opening.RelatedSaleNumber);
        Assert.Equal(7m, sale.Balance);

        var back = page.Entries[2];
        Assert.Equal(StockMovementType.Return, back.Type);
        Assert.Equal(1m, back.In);
        Assert.Equal(StockCardDocumentKind.Return, back.DocumentKind);
        Assert.Equal(returned.Value!.Number.Value, back.DocumentNumber);
        Assert.Equal(completed.Value.Number.Value, back.RelatedSaleNumber);   // a return opens the ORIGINAL invoice
        Assert.Equal(8m, back.Balance);

        // the FIFO cost of each movement: 10 × 2,000,000 in, 3 × 2,000,000 out, 1 × 2,000,000 back in
        Assert.Equal(20_000_000, opening.Value!.Value.Rials);
        Assert.Equal(6_000_000, sale.Value!.Value.Rials);
        Assert.Equal(2_000_000, back.Value!.Value.Rials);
        Assert.Equal([20_000_000L, 14_000_000L, 16_000_000L], page.Entries.Select(entry => entry.BalanceValue!.Value.Rials));
        Assert.Equal(16_000_000, page.ClosingValue!.Value.Rials);
        Assert.Equal(0, page.UnvaluedMovements);

        Assert.Equal(8m, page.ClosingBalance);
        Assert.Equal(11m, page.TotalIn);
        Assert.Equal(3m, page.TotalOut);

        // the period filter: only what happened from the sale on — opening balance is the 10 already there
        var fromSale = await card.ExecuteAsync(new StockCardQuery(product, FromUtc: sale.OccurredAtUtc), CancellationToken.None);
        Assert.Equal(10m, fromSale!.OpeningBalance);
        Assert.Equal(2, fromSale.Entries.Count);

        // an unknown product has no card
        Assert.Null(await card.ExecuteAsync(new StockCardQuery(ERP.Domain.Catalog.ProductId.New()), CancellationToken.None));
    }

    private sealed record TestUserContext(Guid UserId) : IUserContext;

    private sealed class TestClock : IClock
    {
        public required DateTimeOffset UtcNow { get; set; }
    }
}
