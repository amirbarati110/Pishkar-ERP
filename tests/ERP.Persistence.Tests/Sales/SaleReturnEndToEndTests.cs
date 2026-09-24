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

namespace ERP.Persistence.Tests.Sales;

/// <summary>
/// «مرجوعی» روی Firebird واقعی: فروش نقدی ← پیدا کردن فاکتور با شماره ← دو مرجوعی
/// پشت‌سرهم که روی‌هم کل فاکتور می‌شوند ← موجودی با بهای اصلی برمی‌گردد ←
/// مرجوعی سوم رد می‌شود ← نقدی برگشتی از صندوق شیفت کم می‌شود.
/// </summary>
public sealed class SaleReturnEndToEndTests
{
    [Fact]
    public async Task SellFindReturnInTwoPartsStockComesBackAtOriginalCostAndTheShiftCountsTheRefunds()
    {
        await using var database = FirebirdTestDatabase.Create();
        var factory = new FirebirdConnectionFactory(database.Options);
        var defaults = await new FirebirdDatabaseBootstrapper(factory).InitializeAsync(CancellationToken.None);
        var userContext = new TestUserContext(Guid.NewGuid());
        var clock = new TestClock { UtcNow = new DateTimeOffset(2026, 9, 18, 7, 0, 0, TimeSpan.Zero) };

        var retailSetup = new FirebirdRetailSetupService(factory, userContext, clock, AllowAllAccess.Instance);
        var sales = new FirebirdSalesService(factory, userContext, clock, AllowAllAccess.Instance);
        var cashiering = new FirebirdCashieringService(factory, userContext, clock);

        var category = await retailSetup.ExecuteAsync(new CreateCategoryCommand("مواد غذایی", null, 1), CancellationToken.None);
        var product = await retailSetup.ExecuteAsync(
            new CreateProductCommand("برنج ایرانی", "RICE-RET-1", category.Value, defaults.EachUnitId, Money.FromTomans(245_000), ["6260000009902"]),
            CancellationToken.None);
        Assert.True(product.IsSuccess);
        var opening = await retailSetup.ExecuteAsync(
            new ReceiveOpeningStockCommand(product.Value, defaults.MainWarehouseId, 10, 200_000_0, new DateOnly(2026, 9, 1)),
            CancellationToken.None);
        Assert.True(opening.IsSuccess);

        var shift = await cashiering.ExecuteAsync(new OpenCashShiftCommand(defaults.MainWarehouseId, 200_000), CancellationToken.None);
        Assert.True(shift.IsSuccess);

        clock.UtcNow = clock.UtcNow.AddMinutes(5);
        var started = await sales.ExecuteAsync(new StartSaleCommand(defaults.MainWarehouseId), CancellationToken.None);
        await sales.ExecuteAsync(new AddSaleLineCommand(started.Value, product.Value, 3), CancellationToken.None);
        var completed = await sales.ExecuteAsync(
            new CompleteSaleCommand(started.Value, PaymentMethod.Cash, DiscountRials: 0, TaxRatePercent: 10), CancellationToken.None);
        Assert.True(completed.IsSuccess);
        Assert.Equal(808_500, completed.Value!.Totals.Total.ToTomansExact());

        // پیدا کردن فاکتور با شماره‌ای که مشتری در دست دارد
        var found = await sales.ExecuteAsync(new FindSaleForReturnQuery(completed.Value.Number.Value), CancellationToken.None);
        Assert.True(found.IsSuccess);
        Assert.Equal(started.Value, found.Value);
        var missing = await sales.ExecuteAsync(new FindSaleForReturnQuery(999_999), CancellationToken.None);
        Assert.False(missing.IsSuccess);

        var returnable = await sales.ExecuteAsync(new GetReturnableSaleQuery(started.Value), CancellationToken.None);
        var row = Assert.Single(returnable.Value!.Lines);
        Assert.Equal(3, row.RemainingQuantity);
        Assert.Equal("برنج ایرانی", row.ProductName);

        // مرجوعی اول: ۱ عدد
        clock.UtcNow = clock.UtcNow.AddMinutes(5);
        var first = await sales.ExecuteAsync(
            new CompleteSaleReturnCommand(started.Value, [new ReturnLineRequest(product.Value, 1, ReturnDisposition.ToStock)], PaymentMethod.Cash, "اندازه نبود"),
            CancellationToken.None);
        Assert.True(first.IsSuccess, first.Error?.Message);
        Assert.Equal(269_500, first.Value!.Refund.ToTomansExact()); // ۲۴۵٬۰۰۰ + ۱۰٪
        Assert.Equal(1, first.Value.Number.Value);

        await AssertStockAsync(factory, product.Value, defaults.MainWarehouseId, expectedAvailable: 8, expectedLastLayerCostRials: 2_000_000);

        // مرجوعی دوم: باقی‌مانده‌ی ۲ عدد — روی‌هم دقیقاً کل فاکتور
        var second = await sales.ExecuteAsync(
            new CompleteSaleReturnCommand(started.Value, [new ReturnLineRequest(product.Value, 2, ReturnDisposition.ToStock)], PaymentMethod.Cash, "بقیه"),
            CancellationToken.None);
        Assert.True(second.IsSuccess, second.Error?.Message);
        Assert.Equal(808_500, first.Value.Refund.ToTomansExact() + second.Value!.Refund.ToTomansExact());
        await AssertStockAsync(factory, product.Value, defaults.MainWarehouseId, expectedAvailable: 10, expectedLastLayerCostRials: 2_000_000);

        // مرجوعی سوم: دیگر چیزی نمانده
        var third = await sales.ExecuteAsync(
            new CompleteSaleReturnCommand(started.Value, [new ReturnLineRequest(product.Value, 1, ReturnDisposition.ToStock)], PaymentMethod.Cash, "زیاد"),
            CancellationToken.None);
        Assert.False(third.IsSuccess);
        await AssertStockAsync(factory, product.Value, defaults.MainWarehouseId, expectedAvailable: 10, expectedLastLayerCostRials: 2_000_000);

        // فاکتوری که مرجوعی دارد دیگر اصلاح‌شدنی نیست
        var correction = await sales.ExecuteAsync(new StartSaleCorrectionCommand(started.Value, "اشتباه"), CancellationToken.None);
        Assert.False(correction.IsSuccess);
        Assert.Equal("sales.correction.has-returns", correction.Error?.Code);

        // شیفت: ۲۰۰٬۰۰۰ + فروش نقدی ۸۰۸٬۵۰۰ − مرجوعی نقدی ۸۰۸٬۵۰۰ = ۲۰۰٬۰۰۰
        clock.UtcNow = clock.UtcNow.AddMinutes(5);
        var closed = await cashiering.ExecuteAsync(new CloseCashShiftCommand(shift.Value, 200_000, null), CancellationToken.None);
        Assert.True(closed.IsSuccess, closed.Error?.Message);
        Assert.Equal(808_500, closed.Value!.CashSalesDuringShift.ToTomansExact());
        Assert.Equal(808_500, closed.Value.CashRefundsDuringShift.ToTomansExact());
        Assert.Equal(0, closed.Value.VarianceRials);
    }

    private static async Task AssertStockAsync(
        FirebirdConnectionFactory factory,
        Domain.Catalog.ProductId product,
        WarehouseId warehouse,
        decimal expectedAvailable,
        long expectedLastLayerCostRials)
    {
        await using var unitOfWork = await FirebirdUnitOfWork.CreateAsync(factory, CancellationToken.None);
        var ledger = await new FirebirdStockLedgerRepository(unitOfWork).GetAsync(product, warehouse, CancellationToken.None);
        Assert.NotNull(ledger);
        Assert.Equal(expectedAvailable, ledger!.AvailableQuantity.Value);
        Assert.Equal(expectedLastLayerCostRials, ledger.Layers.OrderBy(layer => layer.ReceivedOn).Last().UnitCost.Rials);
    }

    private sealed record TestUserContext(Guid UserId) : IUserContext;

    private sealed class TestClock : IClock
    {
        public required DateTimeOffset UtcNow { get; set; }
    }
}
