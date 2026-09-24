using ERP.Domain.Accounting;
using ERP.Application.Accounting;
using ERP.Application.Catalog;
using ERP.Application.Common;
using ERP.Application.Inventory;
using ERP.Application.Sales;
using ERP.Domain.Common;
using ERP.Domain.Sales;
using ERP.Persistence.Accounting;
using ERP.Persistence.Database;
using ERP.Persistence.Inventory;
using ERP.Persistence.Sales;
using ERP.Persistence.Services;
using ERP.Persistence.Tests.TestSupport;

namespace ERP.Persistence.Tests.Sales;

/// <summary>
/// «تست Idempotency برای ثبت دوباره فرمان فروش» (§13) — re-sending
/// <see cref="CompleteSaleCommand"/> for a sale the first call already
/// completed (e.g. a UI double-click, or a retried request after a dropped
/// response) must fail cleanly, not consume stock or post a journal entry a
/// second time.
/// </summary>
public sealed class CompleteSaleIdempotencyTests
{
    [Fact]
    public async Task CompletingTheSameSaleTwiceOnlyEverConsumesStockAndPostsAJournalEntryOnce()
    {
        var database = FirebirdTestDatabase.Create();
        await using var _ = database;
        var factory = new FirebirdConnectionFactory(database.Options);
        var defaults = await new FirebirdDatabaseBootstrapper(factory).InitializeAsync(CancellationToken.None);
        var userContext = new TestUserContext(Guid.NewGuid());
        var clock = new TestClock(new DateTimeOffset(2026, 9, 18, 8, 0, 0, TimeSpan.Zero));

        var setup = new FirebirdRetailSetupService(factory, userContext, clock, AllowAllAccess.Instance);
        var category = await setup.ExecuteAsync(new CreateCategoryCommand("مواد غذایی", null, 1), CancellationToken.None);
        var product = await setup.ExecuteAsync(
            new CreateProductCommand("برنج ایرانی", "RICE-ID-1", category.Value, defaults.EachUnitId, Money.FromTomans(245_000), ["6260000009920"]),
            CancellationToken.None);
        await setup.ExecuteAsync(
            new ReceiveOpeningStockCommand(product.Value, defaults.MainWarehouseId, 10, 200_000_0, new DateOnly(2026, 9, 1)),
            CancellationToken.None);

        var sales = new FirebirdSalesService(factory, userContext, clock, AllowAllAccess.Instance);
        var accounting = new FirebirdAccountingService(factory);
        var started = await sales.ExecuteAsync(new StartSaleCommand(defaults.MainWarehouseId), CancellationToken.None);
        await sales.ExecuteAsync(new AddSaleLineCommand(started.Value, product.Value, 2), CancellationToken.None);

        var firstAttempt = await sales.ExecuteAsync(
            new CompleteSaleCommand(started.Value, PaymentMethod.Cash, DiscountRials: 0, TaxRatePercent: 0), CancellationToken.None);
        Assert.True(firstAttempt.IsSuccess);

        // همان فرمان، دوباره — مثل یک درخواست تکراری از رابط کاربری یا شبکه.
        var secondAttempt = await sales.ExecuteAsync(
            new CompleteSaleCommand(started.Value, PaymentMethod.Cash, DiscountRials: 0, TaxRatePercent: 0), CancellationToken.None);

        Assert.False(secondAttempt.IsSuccess);
        Assert.Equal("sales.sale.invalid", secondAttempt.Error?.Code);

        await using var unitOfWork = await FirebirdUnitOfWork.CreateAsync(factory, CancellationToken.None);
        var ledger = await new FirebirdStockLedgerRepository(unitOfWork).GetAsync(product.Value, defaults.MainWarehouseId, CancellationToken.None);
        Assert.Equal(8, ledger!.AvailableQuantity.Value); // ۱۰ − ۲، نه ۱۰ − ۴

        var reloadedSale = await new FirebirdSaleRepository(unitOfWork).GetAsync(started.Value, CancellationToken.None);
        Assert.Equal(firstAttempt.Value!.Number, reloadedSale!.Number); // شماره‌ی فاکتور همان بار اول ماند

        var journal = await accounting.ExecuteAsync(new GetSaleJournalEntryQuery(started.Value), CancellationToken.None);
        Assert.NotNull(journal);
        Assert.Equal(490_000, journal!.Lines.Single(line => line.Account == AccountCode.Cash).Debit.ToTomansExact()); // یک سند، نه دوتا با مبلغ دوبرابر
    }

    private sealed record TestUserContext(Guid UserId) : IUserContext;

    private sealed record TestClock(DateTimeOffset UtcNow) : IClock;
}
