using ERP.Application.Catalog;
using ERP.Application.Common;
using ERP.Application.Customers;
using ERP.Application.Inventory;
using ERP.Application.Sales;
using ERP.Domain.Common;
using ERP.Domain.Customers;
using ERP.Domain.Sales;
using ERP.Persistence.Customers;
using ERP.Persistence.Database;
using ERP.Persistence.Sales;
using ERP.Persistence.Services;
using ERP.Persistence.Tests.TestSupport;

namespace ERP.Persistence.Tests.Sales;

/// <summary>
/// <see cref="FirebirdSalesService.ExecuteAsync(GetLineEditInfoQuery, CancellationToken)"/>
/// — the SQL behind the pencil window's «خرید آخر / بهای تمام‌شده / آخرین
/// قیمت فروش به این مشتری» (§6.10), exercised against a real Firebird
/// database so its FIRST-1 subqueries are checked for real, not assumed.
/// </summary>
public sealed class LineEditInfoReaderTests
{
    [Fact]
    public async Task CurrentCostIsTheOldestRemainingLayerAndLastPurchaseIsTheNewestLayer()
    {
        var context = await SetupAsync();

        // Two receipts at different costs and dates: FIFO must still be
        // drawing from the older layer (still has stock), while «خرید آخر»
        // must report the newer one regardless of what is left of it.
        await context.Setup.ExecuteAsync(
            new ReceiveOpeningStockCommand(context.RiceId, context.Defaults.MainWarehouseId, 5, 200_000_0, new DateOnly(2026, 9, 1)),
            CancellationToken.None);
        await context.Setup.ExecuteAsync(
            new ReceiveOpeningStockCommand(context.RiceId, context.Defaults.MainWarehouseId, 5, 210_000_0, new DateOnly(2026, 9, 10)),
            CancellationToken.None);

        var info = await context.Sales.ExecuteAsync(
            new GetLineEditInfoQuery(context.Defaults.MainWarehouseId, context.RiceId, null),
            CancellationToken.None);

        Assert.Equal(200_000, info.CurrentCost!.Value.ToTomansExact());
        Assert.Equal(210_000, info.LastPurchaseCost!.Value.ToTomansExact());
        Assert.Null(info.LastSalePriceToCustomer);
    }

    [Fact]
    public async Task CurrentCostSkipsAnExhaustedOlderLayer()
    {
        var context = await SetupAsync();

        await context.Setup.ExecuteAsync(
            new ReceiveOpeningStockCommand(context.RiceId, context.Defaults.MainWarehouseId, 2, 200_000_0, new DateOnly(2026, 9, 1)),
            CancellationToken.None);
        await context.Setup.ExecuteAsync(
            new ReceiveOpeningStockCommand(context.RiceId, context.Defaults.MainWarehouseId, 5, 210_000_0, new DateOnly(2026, 9, 10)),
            CancellationToken.None);

        // Sell exactly the older layer's two units so it is left with zero.
        var start = await context.Sales.ExecuteAsync(new StartSaleCommand(context.Defaults.MainWarehouseId), CancellationToken.None);
        await context.Sales.ExecuteAsync(new AddSaleLineCommand(start.Value, context.RiceId, 2), CancellationToken.None);
        await context.Sales.ExecuteAsync(
            new CompleteSaleCommand(start.Value, PaymentMethod.Cash, DiscountRials: 0, TaxRatePercent: 0),
            CancellationToken.None);

        var info = await context.Sales.ExecuteAsync(
            new GetLineEditInfoQuery(context.Defaults.MainWarehouseId, context.RiceId, null),
            CancellationToken.None);

        // The oldest layer is gone (REMAINING_QTY = 0); cost must move to the newer one.
        Assert.Equal(210_000, info.CurrentCost!.Value.ToTomansExact());
        Assert.Equal(210_000, info.LastPurchaseCost!.Value.ToTomansExact());
    }

    [Fact]
    public async Task LastSalePriceOnlyMatchesThatExactCustomer()
    {
        var context = await SetupAsync();
        await context.Setup.ExecuteAsync(
            new ReceiveOpeningStockCommand(context.RiceId, context.Defaults.MainWarehouseId, 10, 200_000_0, new DateOnly(2026, 9, 1)),
            CancellationToken.None);

        var customers = new FirebirdCustomerService(context.Factory, context.UserContext, context.Clock);
        var ali = await customers.ExecuteAsync(new QuickCreateCustomerCommand("علی رضایی", "09121112233"), CancellationToken.None);
        var sara = await customers.ExecuteAsync(new QuickCreateCustomerCommand("سارا احمدی", "09124445566"), CancellationToken.None);

        var start = await context.Sales.ExecuteAsync(new StartSaleCommand(context.Defaults.MainWarehouseId), CancellationToken.None);
        await context.Sales.ExecuteAsync(new AddSaleLineCommand(start.Value, context.RiceId, 1), CancellationToken.None);
        await context.Sales.ExecuteAsync(new SetSaleCustomerCommand(start.Value, ali.Value), CancellationToken.None);
        await context.Sales.ExecuteAsync(
            new CompleteSaleCommand(start.Value, PaymentMethod.Cash, DiscountRials: 0, TaxRatePercent: 0),
            CancellationToken.None);

        var forAli = await context.Sales.ExecuteAsync(
            new GetLineEditInfoQuery(context.Defaults.MainWarehouseId, context.RiceId, ali.Value),
            CancellationToken.None);
        var forSara = await context.Sales.ExecuteAsync(
            new GetLineEditInfoQuery(context.Defaults.MainWarehouseId, context.RiceId, sara.Value),
            CancellationToken.None);
        var forCash = await context.Sales.ExecuteAsync(
            new GetLineEditInfoQuery(context.Defaults.MainWarehouseId, context.RiceId, null),
            CancellationToken.None);

        Assert.Equal(245_000, forAli.LastSalePriceToCustomer!.Value.ToTomansExact());
        Assert.Null(forSara.LastSalePriceToCustomer);
        Assert.Null(forCash.LastSalePriceToCustomer);
    }

    [Fact]
    public async Task ANeverReceivedProductComesBackWithNothingRatherThanFailing()
    {
        var context = await SetupAsync();

        var info = await context.Sales.ExecuteAsync(
            new GetLineEditInfoQuery(context.Defaults.MainWarehouseId, context.RiceId, null),
            CancellationToken.None);

        Assert.Null(info.LastPurchaseCost);
        Assert.Null(info.CurrentCost);
        Assert.Null(info.LastSalePriceToCustomer);
    }

    private static async Task<TestContext> SetupAsync()
    {
        var database = FirebirdTestDatabase.Create();
        var factory = new FirebirdConnectionFactory(database.Options);
        var defaults = await new FirebirdDatabaseBootstrapper(factory).InitializeAsync(CancellationToken.None);
        var userContext = new TestUserContext(Guid.NewGuid());
        var clock = new TestClock(new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.Zero));

        var setup = new FirebirdRetailSetupService(factory, userContext, clock);
        var category = await setup.ExecuteAsync(new CreateCategoryCommand("مواد غذایی", null, 1), CancellationToken.None);
        var rice = await setup.ExecuteAsync(
            new CreateProductCommand(
                "برنج ایرانی", "RICE-1", category.Value, defaults.EachUnitId,
                Money.FromTomans(245_000), ["6260000009001"]),
            CancellationToken.None);

        return new TestContext(
            database, factory, defaults, setup, userContext, clock,
            rice.Value, new FirebirdSalesService(factory, userContext, clock));
    }

    private sealed record TestContext(
        FirebirdTestDatabase Database,
        FirebirdConnectionFactory Factory,
        RetailSetupDefaults Defaults,
        FirebirdRetailSetupService Setup,
        IUserContext UserContext,
        IClock Clock,
        ERP.Domain.Catalog.ProductId RiceId,
        FirebirdSalesService Sales);

    private sealed record TestUserContext(Guid UserId) : IUserContext;

    private sealed record TestClock(DateTimeOffset UtcNow) : IClock;
}
