using ERP.Application.Catalog;
using ERP.Application.Common;
using ERP.Application.Inventory;
using ERP.Application.Sales;
using ERP.Domain.Common;
using ERP.Domain.Sales;
using ERP.Persistence.Database;
using ERP.Persistence.Inventory;
using ERP.Persistence.Sales;
using ERP.Persistence.Services;
using ERP.Persistence.Tests.TestSupport;

namespace ERP.Persistence.Tests.Sales;

/// <summary>
/// «تست Rollback با ایجاد خطای عمدی در میانه ثبت فروش» (§13) on real
/// Firebird. A two-line sale where the FIRST line has enough stock (its FIFO
/// consumption and <c>SaveAsync</c> genuinely execute, mid-transaction) and
/// the SECOND line does not — proving the whole transaction discards even
/// the first line's already-executed write, not just that completion failed.
/// </summary>
public sealed class CompleteSaleRollbackTests
{
    [Fact]
    public async Task AFailingSecondLineLeavesTheFirstLinesStockUntouched()
    {
        var database = FirebirdTestDatabase.Create();
        await using var _ = database;
        var factory = new FirebirdConnectionFactory(database.Options);
        var defaults = await new FirebirdDatabaseBootstrapper(factory).InitializeAsync(CancellationToken.None);
        var userContext = new TestUserContext(Guid.NewGuid());
        var clock = new TestClock(new DateTimeOffset(2026, 9, 18, 8, 0, 0, TimeSpan.Zero));

        var setup = new FirebirdRetailSetupService(factory, userContext, clock);
        var category = await setup.ExecuteAsync(new CreateCategoryCommand("مواد غذایی", null, 1), CancellationToken.None);

        var plentiful = await setup.ExecuteAsync(
            new CreateProductCommand("برنج ایرانی", "RICE-RB-1", category.Value, defaults.EachUnitId, Money.FromTomans(245_000), ["6260000009911"]),
            CancellationToken.None);
        await setup.ExecuteAsync(
            new ReceiveOpeningStockCommand(plentiful.Value, defaults.MainWarehouseId, 10, 200_000_0, new DateOnly(2026, 9, 1)),
            CancellationToken.None);

        var scarce = await setup.ExecuteAsync(
            new CreateProductCommand("روغن نایاب", "OIL-RB-1", category.Value, defaults.EachUnitId, Money.FromTomans(180_000), ["6260000009912"]),
            CancellationToken.None);
        await setup.ExecuteAsync(
            new ReceiveOpeningStockCommand(scarce.Value, defaults.MainWarehouseId, 1, 150_000_0, new DateOnly(2026, 9, 1)),
            CancellationToken.None);

        var sales = new FirebirdSalesService(factory, userContext, clock);
        var started = await sales.ExecuteAsync(new StartSaleCommand(defaults.MainWarehouseId), CancellationToken.None);
        await sales.ExecuteAsync(new AddSaleLineCommand(started.Value, plentiful.Value, 2), CancellationToken.None); // خط اول: موجودی کافی
        await sales.ExecuteAsync(new AddSaleLineCommand(started.Value, scarce.Value, 5), CancellationToken.None); // خط دوم: فقط ۱ موجود است

        var completed = await sales.ExecuteAsync(
            new CompleteSaleCommand(started.Value, PaymentMethod.Cash, DiscountRials: 0, TaxRatePercent: 0), CancellationToken.None);

        Assert.False(completed.IsSuccess);
        Assert.Equal("sales.sale.insufficient-stock", completed.Error?.Code);

        await using var unitOfWork = await FirebirdUnitOfWork.CreateAsync(factory, CancellationToken.None);
        var ledgers = new FirebirdStockLedgerRepository(unitOfWork);

        var plentifulLedger = await ledgers.GetAsync(plentiful.Value, defaults.MainWarehouseId, CancellationToken.None);
        Assert.Equal(10, plentifulLedger!.AvailableQuantity.Value); // خط اول هم رول‌بک شد، نه فقط خط دوم

        var scarceLedger = await ledgers.GetAsync(scarce.Value, defaults.MainWarehouseId, CancellationToken.None);
        Assert.Equal(1, scarceLedger!.AvailableQuantity.Value);

        var reloadedSale = await new FirebirdSaleRepository(unitOfWork).GetAsync(started.Value, CancellationToken.None);
        Assert.Equal(SaleStatus.Draft, reloadedSale!.Status); // فاکتور هرگز تکمیل‌شده ثبت نشد
    }

    private sealed record TestUserContext(Guid UserId) : IUserContext;

    private sealed record TestClock(DateTimeOffset UtcNow) : IClock;
}
