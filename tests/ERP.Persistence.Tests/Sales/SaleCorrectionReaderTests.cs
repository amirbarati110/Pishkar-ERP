using ERP.Application.Catalog;
using ERP.Application.Common;
using ERP.Application.Customers;
using ERP.Application.Inventory;
using ERP.Application.Sales;
using ERP.Domain.Catalog;
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
/// «صورتحساب اصلاحی» (§10.10 / Codex rule 15) on a real Firebird database:
/// the original's own row must stay exactly as it was posted, while every
/// place that aggregates completed sales (day totals/list, customer debt)
/// must count only the correction once one exists.
/// </summary>
public sealed class SaleCorrectionReaderTests
{
    [Fact]
    public async Task TheOriginalIsUntouchedAndOnlyTheCorrectionCountsInTodaysListAndTotal()
    {
        var context = await SetupAsync();

        var start = await context.Sales.ExecuteAsync(new StartSaleCommand(context.Defaults.MainWarehouseId), CancellationToken.None);
        var saleId = start.Value;
        await context.Sales.ExecuteAsync(new AddSaleLineCommand(saleId, context.RiceId, 2), CancellationToken.None);
        var completed = await context.Sales.ExecuteAsync(
            new CompleteSaleCommand(saleId, PaymentMethod.Cash, DiscountRials: 0, TaxRatePercent: 0), CancellationToken.None);
        Assert.True(completed.IsSuccess);
        var originalTotal = completed.Value!.Totals.Total.ToTomansExact(); // ۲ × ۲۴۵٬۰۰۰ = ۴۹۰٬۰۰۰

        var startCorrection = await context.Sales.ExecuteAsync(
            new StartSaleCorrectionCommand(saleId, "روش پرداخت اشتباه بود"), CancellationToken.None);
        Assert.True(startCorrection.IsSuccess);
        var correctionId = startCorrection.Value;
        var completeCorrection = await context.Sales.ExecuteAsync(
            new CompleteSaleCorrectionCommand(correctionId, PaymentMethod.Card, TaxRatePercent: 0), CancellationToken.None);
        Assert.True(completeCorrection.IsSuccess);
        var correctionNumber = completeCorrection.Value!.Number;

        // The original's own row: untouched.
        var reloadedOriginal = await context.SaleRepository.GetAsync(saleId, CancellationToken.None);
        Assert.Equal(completed.Value.Number, reloadedOriginal!.Number);
        Assert.Equal(PaymentMethod.Cash, reloadedOriginal.PaymentMethod);
        Assert.Equal(originalTotal, reloadedOriginal.Totals!.Total.ToTomansExact());
        Assert.Null(reloadedOriginal.CorrectsSaleId);

        var reloadedCorrection = await context.SaleRepository.GetAsync(correctionId, CancellationToken.None);
        Assert.Equal(saleId, reloadedCorrection!.CorrectsSaleId);
        Assert.Equal("روش پرداخت اشتباه بود", reloadedCorrection.Note);

        var today = PersianDate.FromUtc(context.Clock.UtcNow);
        var day = await context.Sales.ExecuteAsync(new ListSalesOfDayQuery(today), CancellationToken.None);

        // Exactly one row for this chain — the correction — not the original too.
        var row = Assert.Single(day.Sales, item => item.SaleId == saleId || item.SaleId == correctionId);
        Assert.Equal(correctionId, row.SaleId);
        Assert.Equal(correctionNumber, row.Number);
        Assert.Equal(PaymentMethod.Card, row.PaymentMethod);
        Assert.Equal(originalTotal, day.Total.ToTomansExact()); // not double-counted
    }

    [Fact]
    public async Task CustomerDebtCountsOnlyTheCorrectedAmountOnce()
    {
        var context = await SetupAsync();
        var customers = new FirebirdCustomerService(context.Factory, context.UserContext, context.Clock, AllowAllAccess.Instance);
        var created = await customers.ExecuteAsync(
            new QuickCreateCustomerCommand("محمد رضایی", "09123456789"), CancellationToken.None);
        var customerId = created.Value;

        var start = await context.Sales.ExecuteAsync(new StartSaleCommand(context.Defaults.MainWarehouseId), CancellationToken.None);
        var saleId = start.Value;
        await context.Sales.ExecuteAsync(new AddSaleLineCommand(saleId, context.RiceId, 1), CancellationToken.None);
        await context.Sales.ExecuteAsync(new SetSaleCustomerCommand(saleId, customerId), CancellationToken.None);
        await context.Sales.ExecuteAsync(
            new CompleteSaleCommand(saleId, PaymentMethod.Credit, DiscountRials: 0, TaxRatePercent: 0), CancellationToken.None);

        var beforeCorrection = await customers.ExecuteAsync(new GetCustomerAccountQuery(customerId), CancellationToken.None);
        Assert.Equal(245_000, beforeCorrection.Value!.Debt.ToTomansExact());

        // اصلاحیه: تخفیف ۴۵٬۰۰۰ اضافه شد — بدهی باید به مقدار جدید برسد، نه جمع دو فاکتور.
        var startCorrection = await context.Sales.ExecuteAsync(
            new StartSaleCorrectionCommand(saleId, "تخفیف فراموش شده بود"), CancellationToken.None);
        var correctionId = startCorrection.Value;
        await context.Sales.ExecuteAsync(
            new SetSaleChargesCommand(correctionId, DiscountRials: Money.FromTomans(45_000).Rials, ServiceChargeRials: 0),
            CancellationToken.None);
        var completeCorrection = await context.Sales.ExecuteAsync(
            new CompleteSaleCorrectionCommand(correctionId, PaymentMethod.Credit, TaxRatePercent: 0), CancellationToken.None);
        Assert.True(completeCorrection.IsSuccess);

        var afterCorrection = await customers.ExecuteAsync(new GetCustomerAccountQuery(customerId), CancellationToken.None);
        Assert.Equal(200_000, afterCorrection.Value!.Debt.ToTomansExact()); // ۲۴۵٬۰۰۰ − ۴۵٬۰۰۰، نه ۴۴۵٬۰۰۰
        Assert.Single(afterCorrection.Value.OpenInvoiceNumbers);
    }

    [Fact]
    public async Task StartingASecondCorrectionOnTheSameOriginalIsRefused()
    {
        var context = await SetupAsync();
        var start = await context.Sales.ExecuteAsync(new StartSaleCommand(context.Defaults.MainWarehouseId), CancellationToken.None);
        var saleId = start.Value;
        await context.Sales.ExecuteAsync(new AddSaleLineCommand(saleId, context.RiceId, 1), CancellationToken.None);
        await context.Sales.ExecuteAsync(
            new CompleteSaleCommand(saleId, PaymentMethod.Cash, DiscountRials: 0, TaxRatePercent: 0), CancellationToken.None);

        var first = await context.Sales.ExecuteAsync(
            new StartSaleCorrectionCommand(saleId, "اول"), CancellationToken.None);
        Assert.True(first.IsSuccess);
        await context.Sales.ExecuteAsync(
            new CompleteSaleCorrectionCommand(first.Value, PaymentMethod.Cash, TaxRatePercent: 0), CancellationToken.None);

        var second = await context.Sales.ExecuteAsync(
            new StartSaleCorrectionCommand(saleId, "دوباره"), CancellationToken.None);

        Assert.False(second.IsSuccess);
        Assert.Equal("sales.correction.already-corrected", second.Error?.Code);
    }

    private static async Task<TestContext> SetupAsync()
    {
        var database = FirebirdTestDatabase.Create();
        var factory = new FirebirdConnectionFactory(database.Options);
        var defaults = await new FirebirdDatabaseBootstrapper(factory).InitializeAsync(CancellationToken.None);
        var userContext = new TestUserContext(Guid.NewGuid());
        var clock = new TestClock(new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.Zero));

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
        return new TestContext(database, factory, defaults, userContext, clock, rice.Value, sales, new SaleReader(factory));
    }

    private sealed record TestContext(
        FirebirdTestDatabase Database,
        FirebirdConnectionFactory Factory,
        RetailSetupDefaults Defaults,
        IUserContext UserContext,
        IClock Clock,
        ProductId RiceId,
        FirebirdSalesService Sales,
        ISaleRepository SaleRepository);

    private sealed record TestUserContext(Guid UserId) : IUserContext;

    private sealed record TestClock(DateTimeOffset UtcNow) : IClock;

    /// <summary>Read-only sale lookup with its own short-lived unit of work per call, same reasoning as <see cref="SalesServiceTests"/>'s own.</summary>
    private sealed class SaleReader : ISaleRepository
    {
        private readonly FirebirdConnectionFactory _factory;

        public SaleReader(FirebirdConnectionFactory factory)
        {
            _factory = factory;
        }

        public async Task<ERP.Domain.Sales.Sale?> GetAsync(SaleId saleId, CancellationToken cancellationToken)
        {
            await using var unitOfWork = await FirebirdUnitOfWork.CreateAsync(_factory, cancellationToken).ConfigureAwait(false);
            return await new FirebirdSaleRepository(unitOfWork).GetAsync(saleId, cancellationToken).ConfigureAwait(false);
        }

        public Task SaveAsync(ERP.Domain.Sales.Sale sale, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Test reader is read-only.");

        public async Task<bool> HasCorrectionAsync(SaleId saleId, CancellationToken cancellationToken)
        {
            await using var unitOfWork = await FirebirdUnitOfWork.CreateAsync(_factory, cancellationToken).ConfigureAwait(false);
            return await new FirebirdSaleRepository(unitOfWork).HasCorrectionAsync(saleId, cancellationToken).ConfigureAwait(false);
        }
    }
}
