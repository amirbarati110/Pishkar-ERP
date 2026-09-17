using ERP.Application.Accounting;
using ERP.Application.Cashiering;
using ERP.Application.Catalog;
using ERP.Application.Common;
using ERP.Application.Inventory;
using ERP.Application.Sales;
using ERP.Domain.Common;
using ERP.Domain.Sales;
using ERP.Persistence.Accounting;
using ERP.Persistence.Cashiering;
using ERP.Persistence.Database;
using ERP.Persistence.Inventory;
using ERP.Persistence.Sales;
using ERP.Persistence.Services;
using ERP.Persistence.Tests.TestSupport;

namespace ERP.Persistence.Tests.EndToEnd;

/// <summary>
/// «سناریوی خودکار حیاتی» (§13 تست و کنترل کیفیت): ساخت کالا، موجودی اولیه،
/// شروع شیفت، فروش نقدی، کاهش موجودی و چاپ مجدد — همه در یک تست، پشت سر هم،
/// روی یک دیتابیس Firebird واقعی. هر قطعه‌ی این مسیر جای دیگری هم تست شده،
/// اما این تست تنها جایی است که ثابت می‌کند همه‌شان واقعاً با هم کار می‌کنند،
/// نه فقط هرکدام جدا.
/// </summary>
public sealed class CriticalPathTests
{
    [Fact]
    public async Task CreateProductReceiveStockOpenShiftSellCashConfirmStockDropAndReprint()
    {
        await using var database = FirebirdTestDatabase.Create();
        var factory = new FirebirdConnectionFactory(database.Options);
        var defaults = await new FirebirdDatabaseBootstrapper(factory).InitializeAsync(CancellationToken.None);
        var userContext = new TestUserContext(Guid.NewGuid());
        var clock = new TestClock { UtcNow = new DateTimeOffset(2026, 9, 18, 7, 0, 0, TimeSpan.Zero) };

        var retailSetup = new FirebirdRetailSetupService(factory, userContext, clock);
        var sales = new FirebirdSalesService(factory, userContext, clock);
        var cashiering = new FirebirdCashieringService(factory, userContext, clock);
        var accounting = new FirebirdAccountingService(factory);

        // ۱) ساخت کالا با بارکد
        var category = await retailSetup.ExecuteAsync(new CreateCategoryCommand("مواد غذایی", null, 1), CancellationToken.None);
        Assert.True(category.IsSuccess);
        var product = await retailSetup.ExecuteAsync(
            new CreateProductCommand(
                "برنج ایرانی", "RICE-CP-1", category.Value, defaults.EachUnitId,
                Money.FromTomans(245_000), ["6260000009901"]),
            CancellationToken.None);
        Assert.True(product.IsSuccess);

        // ۲) موجودی اولیه
        var openingStock = await retailSetup.ExecuteAsync(
            new ReceiveOpeningStockCommand(product.Value, defaults.MainWarehouseId, 10, 200_000_0, new DateOnly(2026, 9, 1)),
            CancellationToken.None);
        Assert.True(openingStock.IsSuccess);

        // ۳) شروع شیفت صندوق
        var shift = await cashiering.ExecuteAsync(
            new OpenCashShiftCommand(defaults.MainWarehouseId, 200_000), CancellationToken.None);
        Assert.True(shift.IsSuccess);

        // ۴) فروش نقدی — سه مرحله‌ی صفحه‌ی فروش: کالا/مشتری → پرداخت → تأیید
        clock.UtcNow = clock.UtcNow.AddMinutes(5); // بعد از شروع شیفت، قبل از فروش — تا فروش دقیقاً روی لحظه‌ی بازشدن شیفت نیفتد
        var started = await sales.ExecuteAsync(new StartSaleCommand(defaults.MainWarehouseId), CancellationToken.None);
        Assert.True(started.IsSuccess);
        var lineAdded = await sales.ExecuteAsync(new AddSaleLineCommand(started.Value, product.Value, 3), CancellationToken.None);
        Assert.True(lineAdded.IsSuccess);
        var completed = await sales.ExecuteAsync(
            new CompleteSaleCommand(started.Value, PaymentMethod.Cash, DiscountRials: 0, TaxRatePercent: 10), CancellationToken.None);
        Assert.True(completed.IsSuccess); // ۳ × ۲۴۵٬۰۰۰ = ۷۳۵٬۰۰۰ + ۱۰٪ = ۸۰۸٬۵۰۰
        Assert.Equal(808_500, completed.Value!.Totals.Total.ToTomansExact());

        // ۵) کاهش موجودی — ۱۰ منهای ۳ باید ۷ بماند
        await using (var unitOfWork = await FirebirdUnitOfWork.CreateAsync(factory, CancellationToken.None))
        {
            var ledger = await new FirebirdStockLedgerRepository(unitOfWork)
                .GetAsync(product.Value, defaults.MainWarehouseId, CancellationToken.None);
            Assert.NotNull(ledger);
            Assert.Equal(7, ledger!.AvailableQuantity.Value);
        }

        // سند حسابداری حداقلی هم پشت همین فروش پست شده باشد
        var journal = await accounting.ExecuteAsync(new GetSaleJournalEntryQuery(started.Value), CancellationToken.None);
        Assert.NotNull(journal);
        Assert.Equal(journal!.Lines.Sum(line => line.Debit.Rials), journal.Lines.Sum(line => line.Credit.Rials));

        // ۶) بستن شیفت — فروش نقدی باید در تطبیق دیده شود
        clock.UtcNow = clock.UtcNow.AddMinutes(5); // بعد از فروش، قبل از بستن شیفت
        var closedShift = await cashiering.ExecuteAsync(
            new CloseCashShiftCommand(shift.Value, 1_008_500, null), CancellationToken.None);
        Assert.True(closedShift.IsSuccess);
        Assert.Equal(808_500, closedShift.Value!.CashSalesDuringShift.ToTomansExact());
        Assert.Equal(0, closedShift.Value.VarianceRials);

        // ۷) چاپ مجدد — همان فاکتور دوباره خوانده می‌شود، چیزی تکرار نمی‌شود
        var reprintedOnce = await sales.ExecuteAsync(new GetSaleDetailsQuery(started.Value), CancellationToken.None);
        var reprintedTwice = await sales.ExecuteAsync(new GetSaleDetailsQuery(started.Value), CancellationToken.None);
        Assert.True(reprintedOnce.IsSuccess);
        Assert.True(reprintedTwice.IsSuccess);
        Assert.Equal(completed.Value.Number, reprintedOnce.Value!.Number);
        Assert.Equal(completed.Value.Number, reprintedTwice.Value!.Number);
        Assert.Equal(started.Value, reprintedOnce.Value.SaleId);

        await using var finalUnitOfWork = await FirebirdUnitOfWork.CreateAsync(factory, CancellationToken.None);
        var finalLedger = await new FirebirdStockLedgerRepository(finalUnitOfWork)
            .GetAsync(product.Value, defaults.MainWarehouseId, CancellationToken.None);
        Assert.Equal(7, finalLedger!.AvailableQuantity.Value); // چاپ مجدد موجودی را دست نمی‌زند
    }

    private sealed record TestUserContext(Guid UserId) : IUserContext;

    private sealed class TestClock : IClock
    {
        public required DateTimeOffset UtcNow { get; set; }
    }
}
