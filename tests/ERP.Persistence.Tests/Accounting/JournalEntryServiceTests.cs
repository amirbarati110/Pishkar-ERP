using ERP.Application.Accounting;
using ERP.Application.Catalog;
using ERP.Application.Common;
using ERP.Application.Inventory;
using ERP.Application.Sales;
using ERP.Domain.Accounting;
using ERP.Domain.Catalog;
using ERP.Domain.Common;
using ERP.Domain.Sales;
using ERP.Persistence.Accounting;
using ERP.Persistence.Database;
using ERP.Persistence.Sales;
using ERP.Persistence.Services;
using ERP.Persistence.Tests.TestSupport;

namespace ERP.Persistence.Tests.Accounting;

/// <summary>«سند حسابداری حداقلی» on real Firebird — proves a completed sale's journal entry round-trips balanced, and that «مشاهده سند حسابداری» (§6.23) reads back exactly what was posted.</summary>
public sealed class JournalEntryServiceTests
{
    [Fact]
    public async Task ACompletedCashSaleWithTaxPostsABalancedEntryWithRevenueVatAndCostLinesReadableAfterward()
    {
        var context = await SetupAsync();

        var start = await context.Sales.ExecuteAsync(new StartSaleCommand(context.Defaults.MainWarehouseId), CancellationToken.None);
        await context.Sales.ExecuteAsync(new AddSaleLineCommand(start.Value, context.RiceId, 2), CancellationToken.None);
        var completed = await context.Sales.ExecuteAsync(
            new CompleteSaleCommand(start.Value, PaymentMethod.Card, DiscountRials: 0, TaxRatePercent: 10), CancellationToken.None);
        Assert.True(completed.IsSuccess); // ۲ × ۲۴۵٬۰۰۰ = ۴۹۰٬۰۰۰ + ۱۰٪ مالیات = ۵۳۹٬۰۰۰

        var view = await context.Accounting.ExecuteAsync(new GetSaleJournalEntryQuery(start.Value), CancellationToken.None);

        Assert.NotNull(view);
        Assert.Equal(JournalSourceType.Sale, view!.SourceType);
        Assert.Equal(5, view.Lines.Count);
        var totalDebit = view.Lines.Sum(line => line.Debit.Rials);
        var totalCredit = view.Lines.Sum(line => line.Credit.Rials);
        Assert.Equal(totalDebit, totalCredit);
        Assert.Equal(5_390_000 + 4_000_000, totalDebit); // فروش + بهای تمام‌شده‌ی FIFO (۲ × ۲٬۰۰۰٬۰۰۰)
        Assert.Contains(view.Lines, line => line.Account == AccountCode.CardClearing && line.Debit.Rials == 5_390_000);
        Assert.Contains(view.Lines, line => line.Account == AccountCode.SalesRevenue && line.Credit.Rials == 4_900_000);
        Assert.Contains(view.Lines, line => line.Account == AccountCode.VatPayable && line.Credit.Rials == 490_000);
        Assert.Contains(view.Lines, line => line.Account == AccountCode.CostOfGoodsSold && line.Debit.Rials == 4_000_000);
        Assert.Contains(view.Lines, line => line.Account == AccountCode.Inventory && line.Credit.Rials == 4_000_000);
    }

    [Fact]
    public async Task ASaleWithNoJournalEntryYetReturnsNull()
    {
        var context = await SetupAsync();

        var view = await context.Accounting.ExecuteAsync(new GetSaleJournalEntryQuery(SaleId.New()), CancellationToken.None);

        Assert.Null(view);
    }

    private static async Task<TestContext> SetupAsync()
    {
        var database = FirebirdTestDatabase.Create();
        var factory = new FirebirdConnectionFactory(database.Options);
        var defaults = await new FirebirdDatabaseBootstrapper(factory).InitializeAsync(CancellationToken.None);
        var userContext = new TestUserContext(Guid.NewGuid());
        var clock = new TestClock(new DateTimeOffset(2026, 9, 17, 10, 0, 0, TimeSpan.Zero));

        var setup = new FirebirdRetailSetupService(factory, userContext, clock, AllowAllAccess.Instance);
        var category = await setup.ExecuteAsync(new CreateCategoryCommand("مواد غذایی", null, 1), CancellationToken.None);
        var rice = await setup.ExecuteAsync(
            new CreateProductCommand(
                "برنج ایرانی", "RICE-1", category.Value, defaults.EachUnitId,
                Money.FromTomans(245_000), ["6260000009002"]),
            CancellationToken.None);
        await setup.ExecuteAsync(
            new ReceiveOpeningStockCommand(rice.Value, defaults.MainWarehouseId, 20, 200_000_0, new DateOnly(2026, 9, 1)),
            CancellationToken.None);

        var sales = new FirebirdSalesService(factory, userContext, clock, AllowAllAccess.Instance);
        var accounting = new FirebirdAccountingService(factory);
        return new TestContext(defaults, rice.Value, sales, accounting);
    }

    private sealed record TestContext(
        RetailSetupDefaults Defaults,
        ProductId RiceId,
        FirebirdSalesService Sales,
        FirebirdAccountingService Accounting);

    private sealed record TestUserContext(Guid UserId) : IUserContext;

    private sealed record TestClock(DateTimeOffset UtcNow) : IClock;
}
