using ERP.Application.Cashiering;
using ERP.Application.Catalog;
using ERP.Application.Common;
using ERP.Application.Inventory;
using ERP.Application.Sales;
using ERP.Domain.Catalog;
using ERP.Domain.Common;
using ERP.Domain.Inventory;
using ERP.Domain.Sales;
using ERP.Persistence.Cashiering;
using ERP.Persistence.Database;
using ERP.Persistence.Sales;
using ERP.Persistence.Services;
using ERP.Persistence.Tests.TestSupport;

namespace ERP.Persistence.Tests.Cashiering;

/// <summary>
/// «شیفت صندوق پایه» on real Firebird — reconciliation only means something
/// if the cash-sales figure it reads back genuinely comes from the SALE
/// table written by the real Sales module, not a canned number.
/// </summary>
public sealed class CashShiftServiceTests
{
    [Fact]
    public async Task OpeningAndClosingASurvivesARoundTripWithACorrectReconciliation()
    {
        var context = await SetupAsync();

        var opened = await context.Cashiering.ExecuteAsync(
            new OpenCashShiftCommand(context.Defaults.MainWarehouseId, 200_000), CancellationToken.None);
        Assert.True(opened.IsSuccess);

        var statusWhileOpen = await context.Cashiering.ExecuteAsync(
            new GetCashShiftStatusQuery(context.Defaults.MainWarehouseId), CancellationToken.None);
        Assert.True(statusWhileOpen.IsOpen);
        Assert.Equal(2_000_000, statusWhileOpen.OpeningCash!.Value.Rials);
        Assert.Equal(0, statusWhileOpen.CashSalesSoFar!.Value.Rials);

        context.Clock.UtcNow = context.Clock.UtcNow.AddMinutes(5);
        var start = await context.Sales.ExecuteAsync(new StartSaleCommand(context.Defaults.MainWarehouseId), CancellationToken.None);
        await context.Sales.ExecuteAsync(new AddSaleLineCommand(start.Value, context.RiceId, 2), CancellationToken.None);
        var cashSale = await context.Sales.ExecuteAsync(
            new CompleteSaleCommand(start.Value, PaymentMethod.Cash, DiscountRials: 0, TaxRatePercent: 0), CancellationToken.None);
        Assert.True(cashSale.IsSuccess); // ۲ × ۲۴۵٬۰۰۰ = ۴۹۰٬۰۰۰

        var start2 = await context.Sales.ExecuteAsync(new StartSaleCommand(context.Defaults.MainWarehouseId), CancellationToken.None);
        await context.Sales.ExecuteAsync(new AddSaleLineCommand(start2.Value, context.RiceId, 1), CancellationToken.None);
        await context.Sales.ExecuteAsync(
            new CompleteSaleCommand(start2.Value, PaymentMethod.Card, DiscountRials: 0, TaxRatePercent: 0), CancellationToken.None);

        context.Clock.UtcNow = context.Clock.UtcNow.AddMinutes(5);
        var closed = await context.Cashiering.ExecuteAsync(
            new CloseCashShiftCommand(opened.Value, 690_000, "همه چیز درست بود"), CancellationToken.None);

        Assert.True(closed.IsSuccess);
        Assert.Equal(490_000, closed.Value!.CashSalesDuringShift.ToTomansExact()); // فقط نقدی، نه کارت
        Assert.Equal(690_000, closed.Value.ExpectedCash.ToTomansExact());
        Assert.Equal(0, closed.Value.VarianceRials);

        var statusAfterClose = await context.Cashiering.ExecuteAsync(
            new GetCashShiftStatusQuery(context.Defaults.MainWarehouseId), CancellationToken.None);
        Assert.False(statusAfterClose.IsOpen); // بسته شد؛ چیزی «باز» نمی‌ماند
    }

    [Fact]
    public async Task TwoWarehousesWithShiftsOpenAtTheSameTimeDoNotMixEachOthersCashSales()
    {
        // Checklist appendix ز.۶ — تا ۱۴۰۵/۰۶/۲۹ جمع فروش نقدی شیفت سراسری بود؛
        // این تست دقیقاً همان سناریو را می‌سازد که آن باگ را نمایان می‌کرد: دو
        // انبار، دو شیفت هم‌زمان باز، فروش نقدی جدا در هرکدام.
        var context = await SetupAsync();
        var setup = new FirebirdRetailSetupService(context.Factory, new TestUserContext(Guid.NewGuid()), context.Clock, AllowAllAccess.Instance);
        var secondWarehouse = await setup.ExecuteAsync(new CreateWarehouseCommand("شعبه‌ی دوم", null), CancellationToken.None);
        Assert.True(secondWarehouse.IsSuccess);
        var secondWarehouseId = secondWarehouse.Value;
        await setup.ExecuteAsync(
            new ReceiveOpeningStockCommand(context.RiceId, secondWarehouseId, 20, 200_000_0, new DateOnly(2026, 9, 1)),
            CancellationToken.None);

        var firstShift = await context.Cashiering.ExecuteAsync(
            new OpenCashShiftCommand(context.Defaults.MainWarehouseId, 200_000), CancellationToken.None);
        var secondShift = await context.Cashiering.ExecuteAsync(
            new OpenCashShiftCommand(secondWarehouseId, 300_000), CancellationToken.None);
        Assert.True(firstShift.IsSuccess);
        Assert.True(secondShift.IsSuccess);

        context.Clock.UtcNow = context.Clock.UtcNow.AddMinutes(5);
        var firstSale = await context.Sales.ExecuteAsync(new StartSaleCommand(context.Defaults.MainWarehouseId), CancellationToken.None);
        await context.Sales.ExecuteAsync(new AddSaleLineCommand(firstSale.Value, context.RiceId, 2), CancellationToken.None);
        await context.Sales.ExecuteAsync(
            new CompleteSaleCommand(firstSale.Value, PaymentMethod.Cash, DiscountRials: 0, TaxRatePercent: 0), CancellationToken.None); // ۴۹۰٬۰۰۰

        var secondSale = await context.Sales.ExecuteAsync(new StartSaleCommand(secondWarehouseId), CancellationToken.None);
        await context.Sales.ExecuteAsync(new AddSaleLineCommand(secondSale.Value, context.RiceId, 5), CancellationToken.None);
        await context.Sales.ExecuteAsync(
            new CompleteSaleCommand(secondSale.Value, PaymentMethod.Cash, DiscountRials: 0, TaxRatePercent: 0), CancellationToken.None); // ۱٬۲۲۵٬۰۰۰

        context.Clock.UtcNow = context.Clock.UtcNow.AddMinutes(5);
        var firstStatus = await context.Cashiering.ExecuteAsync(new GetCashShiftStatusQuery(context.Defaults.MainWarehouseId), CancellationToken.None);
        var secondStatus = await context.Cashiering.ExecuteAsync(new GetCashShiftStatusQuery(secondWarehouseId), CancellationToken.None);

        Assert.Equal(490_000, firstStatus.CashSalesSoFar!.Value.ToTomansExact());
        Assert.Equal(1_225_000, secondStatus.CashSalesSoFar!.Value.ToTomansExact());

        var closedFirst = await context.Cashiering.ExecuteAsync(
            new CloseCashShiftCommand(firstShift.Value, 690_000, null), CancellationToken.None);
        var closedSecond = await context.Cashiering.ExecuteAsync(
            new CloseCashShiftCommand(secondShift.Value, 1_525_000, null), CancellationToken.None);

        Assert.Equal(490_000, closedFirst.Value!.CashSalesDuringShift.ToTomansExact());
        Assert.Equal(0, closedFirst.Value.VarianceRials);
        Assert.Equal(1_225_000, closedSecond.Value!.CashSalesDuringShift.ToTomansExact());
        Assert.Equal(0, closedSecond.Value.VarianceRials);
    }

    [Fact]
    public async Task ASecondShiftCannotOpenWhileTheFirstIsStillOpenEvenFromAFreshConnection()
    {
        var context = await SetupAsync();
        await context.Cashiering.ExecuteAsync(
            new OpenCashShiftCommand(context.Defaults.MainWarehouseId, 200_000), CancellationToken.None);

        var second = await context.Cashiering.ExecuteAsync(
            new OpenCashShiftCommand(context.Defaults.MainWarehouseId, 100_000), CancellationToken.None);

        Assert.False(second.IsSuccess);
        Assert.Equal("cashiering.shift.already-open", second.Error?.Code);
    }

    private static async Task<TestContext> SetupAsync()
    {
        var database = FirebirdTestDatabase.Create();
        var factory = new FirebirdConnectionFactory(database.Options);
        var defaults = await new FirebirdDatabaseBootstrapper(factory).InitializeAsync(CancellationToken.None);
        var userContext = new TestUserContext(Guid.NewGuid());
        var clock = new TestClock { UtcNow = new DateTimeOffset(2026, 9, 17, 8, 0, 0, TimeSpan.Zero) };

        var setup = new FirebirdRetailSetupService(factory, userContext, clock, AllowAllAccess.Instance);
        var category = await setup.ExecuteAsync(new CreateCategoryCommand("مواد غذایی", null, 1), CancellationToken.None);
        var rice = await setup.ExecuteAsync(
            new CreateProductCommand(
                "برنج ایرانی", "RICE-1", category.Value, defaults.EachUnitId,
                Money.FromTomans(245_000), ["6260000009001"]),
            CancellationToken.None);
        await setup.ExecuteAsync(
            new ReceiveOpeningStockCommand(rice.Value, defaults.MainWarehouseId, 20, 200_000_0, new DateOnly(2026, 9, 1)),
            CancellationToken.None);

        var sales = new FirebirdSalesService(factory, userContext, clock, AllowAllAccess.Instance);
        var cashiering = new FirebirdCashieringService(factory, userContext, clock);
        return new TestContext(database, factory, defaults, clock, rice.Value, sales, cashiering);
    }

    private sealed record TestContext(
        FirebirdTestDatabase Database,
        FirebirdConnectionFactory Factory,
        RetailSetupDefaults Defaults,
        TestClock Clock,
        ProductId RiceId,
        FirebirdSalesService Sales,
        FirebirdCashieringService Cashiering);

    private sealed record TestUserContext(Guid UserId) : IUserContext;

    private sealed class TestClock : IClock
    {
        public required DateTimeOffset UtcNow { get; set; }
    }
}
