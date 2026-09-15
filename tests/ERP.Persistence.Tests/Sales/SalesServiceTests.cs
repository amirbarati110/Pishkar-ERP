using ERP.Application.Catalog;
using ERP.Application.Common;
using ERP.Application.Inventory;
using ERP.Application.Sales;
using ERP.Domain.Catalog;
using ERP.Domain.Common;
using ERP.Domain.Sales;
using ERP.Persistence.Catalog;
using ERP.Persistence.Database;
using ERP.Persistence.Inventory;
using ERP.Persistence.Sales;
using ERP.Persistence.Services;
using ERP.Persistence.Tests.TestSupport;

namespace ERP.Persistence.Tests.Sales;

public sealed class SalesServiceTests
{
    [Fact]
    public async Task FullQuickSaleFlowDeductsFifoStockAndPersistsTheCompletedSale()
    {
        var context = await SetupAsync();

        var start = await context.Sales.ExecuteAsync(
            new StartSaleCommand(context.Defaults.MainWarehouseId, CustomerId: null),
            CancellationToken.None);
        Assert.True(start.IsSuccess);
        var saleId = start.Value;

        var firstLine = await context.Sales.ExecuteAsync(
            new AddSaleLineCommand(saleId, context.RiceId, 2),
            CancellationToken.None);
        var secondLine = await context.Sales.ExecuteAsync(
            new AddSaleLineCommand(saleId, context.OilId, 1),
            CancellationToken.None);
        Assert.True(firstLine.IsSuccess);
        Assert.True(secondLine.IsSuccess);

        // Cashier double-scans the rice by mistake, then removes it and re-adds
        // the right quantity — exercising remove + merge-on-add together.
        var duplicateScan = await context.Sales.ExecuteAsync(
            new AddSaleLineCommand(saleId, context.RiceId, 1),
            CancellationToken.None);
        Assert.True(duplicateScan.IsSuccess);

        var complete = await context.Sales.ExecuteAsync(
            new CompleteSaleCommand(saleId, PaymentMethod.Card, DiscountRials: 0, TaxRatePercent: 9),
            CancellationToken.None);

        Assert.True(complete.IsSuccess);
        // 3 × 245,000 (rice) + 1 × 850,000 (oil) = 1,585,000 Tomans subtotal.
        Assert.Equal(1_585_000, complete.Value!.Totals.Subtotal.ToTomansExact());
        Assert.Equal(0, complete.Value.Totals.Discount.Rials);

        var reloaded = await context.SaleRepository.GetAsync(saleId, CancellationToken.None);
        Assert.NotNull(reloaded);
        Assert.Equal(SaleStatus.Completed, reloaded!.Status);
        Assert.Equal(2, reloaded.Lines.Count);
        var riceLine = Assert.Single(reloaded.Lines, line => line.ProductId == context.RiceId);
        Assert.Equal(3, riceLine.Quantity.Value);

        var riceLedger = await context.StockLedgers.GetAsync(
            context.RiceId, context.Defaults.MainWarehouseId, CancellationToken.None);
        Assert.NotNull(riceLedger);
        Assert.Equal(7, riceLedger!.AvailableQuantity.Value); // 10 received − 3 sold
    }

    [Fact]
    public async Task LineDiscountPriceOverrideAndServiceChargeSurviveARoundTrip()
    {
        var context = await SetupAsync();

        var start = await context.Sales.ExecuteAsync(
            new StartSaleCommand(context.Defaults.MainWarehouseId, CustomerId: null),
            CancellationToken.None);
        var saleId = start.Value;
        await context.Sales.ExecuteAsync(
            new AddSaleLineCommand(saleId, context.RiceId, 3),
            CancellationToken.None);

        // مداد: قیمت این فاکتور ۲۰۰٬۰۰۰ (به‌جای ۲۴۵٬۰۰۰) + تخفیف سطر ۵۰٬۰۰۰
        var edit = await context.Sales.ExecuteAsync(
            new ChangeSaleLineCommand(
                saleId, context.RiceId, Quantity: 3,
                UnitPriceRials: Money.FromTomans(200_000).Rials,
                DiscountRials: Money.FromTomans(50_000).Rials),
            CancellationToken.None);
        Assert.True(edit.IsSuccess);

        // خدمات/هزینه ۳۰٬۰۰۰ و تخفیف فاکتور ۲۰٬۰۰۰
        var charges = await context.Sales.ExecuteAsync(
            new SetSaleChargesCommand(
                saleId,
                DiscountRials: Money.FromTomans(20_000).Rials,
                ServiceChargeRials: Money.FromTomans(30_000).Rials),
            CancellationToken.None);
        Assert.True(charges.IsSuccess);

        var reloaded = await context.SaleRepository.GetAsync(saleId, CancellationToken.None);
        var line = Assert.Single(reloaded!.Lines);
        Assert.Equal(200_000, line.UnitPrice.ToTomansExact());
        Assert.Equal(245_000, line.CatalogPrice.ToTomansExact());  // قیمت اصلی کالا دست‌نخورده
        Assert.True(line.PriceOverridden);
        Assert.Equal(50_000, line.Discount.ToTomansExact());
        Assert.Equal(550_000, line.LineTotal.ToTomansExact());     // ۳×۲۰۰٬۰۰۰ − ۵۰٬۰۰۰
        Assert.Equal(30_000, reloaded.ServiceCharge.ToTomansExact());
        Assert.Equal(20_000, reloaded.Discount.ToTomansExact());

        var complete = await context.Sales.ExecuteAsync(
            new CompleteSaleCommand(saleId, PaymentMethod.Cheque, 0, 9),
            CancellationToken.None);
        Assert.True(complete.IsSuccess);
        // (۵۵۰٬۰۰۰ − ۲۰٬۰۰۰ + ۳۰٬۰۰۰) = ۵۶۰٬۰۰۰ → مالیات ۹٪ = ۵۰٬۴۰۰ → ۶۱۰٬۴۰۰
        Assert.Equal(30_000, complete.Value!.Totals.ServiceCharge.ToTomansExact());
        Assert.Equal(50_400, complete.Value.Totals.Tax.ToTomansExact());
        Assert.Equal(610_400, complete.Value.Totals.Total.ToTomansExact());
    }

    [Fact]
    public async Task UpdatingTheCatalogPriceFromTheLineEditorChangesTheProductToo()
    {
        var context = await SetupAsync();
        var start = await context.Sales.ExecuteAsync(
            new StartSaleCommand(context.Defaults.MainWarehouseId, CustomerId: null),
            CancellationToken.None);
        await context.Sales.ExecuteAsync(
            new AddSaleLineCommand(start.Value, context.OilId, 1),
            CancellationToken.None);

        await context.Sales.ExecuteAsync(
            new ChangeSaleLineCommand(
                start.Value, context.OilId, 1,
                UnitPriceRials: Money.FromTomans(900_000).Rials,
                DiscountRials: 0,
                UpdateCatalogPrice: true),
            CancellationToken.None);

        // فاکتور بعدی باید قیمت جدید کالا را بردارد
        var next = await context.Sales.ExecuteAsync(
            new StartSaleCommand(context.Defaults.MainWarehouseId, CustomerId: null),
            CancellationToken.None);
        await context.Sales.ExecuteAsync(
            new AddSaleLineCommand(next.Value, context.OilId, 1),
            CancellationToken.None);
        var reloaded = await context.SaleRepository.GetAsync(next.Value, CancellationToken.None);

        Assert.Equal(900_000, Assert.Single(reloaded!.Lines).UnitPrice.ToTomansExact());
    }

    [Fact]
    public async Task CompletingWithAllowNegativeStockSucceedsAndPersistsTheShortfall()
    {
        var context = await SetupAsync();

        var start = await context.Sales.ExecuteAsync(
            new StartSaleCommand(context.Defaults.MainWarehouseId, CustomerId: null),
            CancellationToken.None);
        var saleId = start.Value;
        // Only 10 rice on record; cashier sells 12 anyway with the override on.
        await context.Sales.ExecuteAsync(
            new AddSaleLineCommand(saleId, context.RiceId, 12),
            CancellationToken.None);

        var complete = await context.Sales.ExecuteAsync(
            new CompleteSaleCommand(saleId, PaymentMethod.Cash, 0, 9, AllowNegativeStock: true),
            CancellationToken.None);

        Assert.True(complete.IsSuccess);
        var reloaded = await context.SaleRepository.GetAsync(saleId, CancellationToken.None);
        Assert.Equal(SaleStatus.Completed, reloaded!.Status);
        var riceLedger = await context.StockLedgers.GetAsync(
            context.RiceId, context.Defaults.MainWarehouseId, CancellationToken.None);
        Assert.Equal(-2, riceLedger!.AvailableQuantity.Value);
    }

    [Fact]
    public async Task CompletingWithInsufficientStockRollsBackTheWholeTransaction()
    {
        var context = await SetupAsync();

        var start = await context.Sales.ExecuteAsync(
            new StartSaleCommand(context.Defaults.MainWarehouseId, CustomerId: null),
            CancellationToken.None);
        var saleId = start.Value;
        await context.Sales.ExecuteAsync(
            new AddSaleLineCommand(saleId, context.RiceId, 999),
            CancellationToken.None);

        var complete = await context.Sales.ExecuteAsync(
            new CompleteSaleCommand(saleId, PaymentMethod.Cash, DiscountRials: 0, TaxRatePercent: 9),
            CancellationToken.None);

        Assert.False(complete.IsSuccess);
        Assert.Equal("sales.sale.invalid", complete.Error?.Code);

        // Nothing about the failed completion should have stuck: the sale is
        // still a Draft and the rice stock is untouched (10 received, 0 sold).
        var reloaded = await context.SaleRepository.GetAsync(saleId, CancellationToken.None);
        Assert.Equal(SaleStatus.Draft, reloaded!.Status);
        var riceLedger = await context.StockLedgers.GetAsync(
            context.RiceId, context.Defaults.MainWarehouseId, CancellationToken.None);
        Assert.Equal(10, riceLedger!.AvailableQuantity.Value);
    }

    [Fact]
    public async Task CompletedSalesGetAscendingNumbersThatSurviveAReloadAndDraftsGetNone()
    {
        var context = await SetupAsync();

        var first = await StartSaleWithAsync(context, context.RiceId, 1);
        var rejected = await StartSaleWithAsync(context, context.RiceId, 999);
        var second = await StartSaleWithAsync(context, context.OilId, 1);

        var firstComplete = await context.Sales.ExecuteAsync(
            new CompleteSaleCommand(first, PaymentMethod.Cash, 0, 9), CancellationToken.None);
        var rejectedComplete = await context.Sales.ExecuteAsync(
            new CompleteSaleCommand(rejected, PaymentMethod.Cash, 0, 9), CancellationToken.None);
        var secondComplete = await context.Sales.ExecuteAsync(
            new CompleteSaleCommand(second, PaymentMethod.Card, 0, 9), CancellationToken.None);

        Assert.True(firstComplete.IsSuccess);
        Assert.False(rejectedComplete.IsSuccess);
        Assert.True(secondComplete.IsSuccess);

        // A sale rejected by a business rule never reaches the sequence, so the
        // next real invoice follows straight on (no hole in the numbering).
        Assert.Equal(firstComplete.Value!.Number.Value + 1, secondComplete.Value!.Number.Value);

        var reloadedFirst = await context.SaleRepository.GetAsync(first, CancellationToken.None);
        var reloadedRejected = await context.SaleRepository.GetAsync(rejected, CancellationToken.None);
        Assert.Equal(firstComplete.Value.Number, reloadedFirst!.Number);
        Assert.Null(reloadedRejected!.Number);
    }

    private static async Task<SaleId> StartSaleWithAsync(TestContext context, ProductId productId, decimal quantity)
    {
        var start = await context.Sales.ExecuteAsync(
            new StartSaleCommand(context.Defaults.MainWarehouseId, CustomerId: null),
            CancellationToken.None);
        await context.Sales.ExecuteAsync(
            new AddSaleLineCommand(start.Value, productId, quantity),
            CancellationToken.None);
        return start.Value;
    }

    private static async Task<TestContext> SetupAsync()
    {
        var database = FirebirdTestDatabase.Create();
        var factory = new FirebirdConnectionFactory(database.Options);
        var defaults = await new FirebirdDatabaseBootstrapper(factory)
            .InitializeAsync(CancellationToken.None);
        var userContext = new TestUserContext(Guid.NewGuid());
        var clock = new TestClock(new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.Zero));

        var setup = new FirebirdRetailSetupService(factory, userContext, clock);
        var category = await setup.ExecuteAsync(
            new CreateCategoryCommand("مواد غذایی", null, 1), CancellationToken.None);

        var rice = await setup.ExecuteAsync(
            new CreateProductCommand(
                "برنج ایرانی", "RICE-1", category.Value, defaults.EachUnitId,
                Money.FromTomans(245_000), ["6260000009001"]),
            CancellationToken.None);
        var oil = await setup.ExecuteAsync(
            new CreateProductCommand(
                "روغن حیوانی", "OIL-1", category.Value, defaults.EachUnitId,
                Money.FromTomans(850_000), ["6260000009002"]),
            CancellationToken.None);

        await setup.ExecuteAsync(
            new ReceiveOpeningStockCommand(
                rice.Value, defaults.MainWarehouseId, 10, 200_000_0, DateOnly.FromDateTime(clock.UtcNow.Date)),
            CancellationToken.None);
        await setup.ExecuteAsync(
            new ReceiveOpeningStockCommand(
                oil.Value, defaults.MainWarehouseId, 10, 700_000_0, DateOnly.FromDateTime(clock.UtcNow.Date)),
            CancellationToken.None);

        var saleRepository = new SaleReader(factory);
        var stockLedgers = new StockLedgerReader(factory);

        return new TestContext(
            database,
            defaults,
            rice.Value,
            oil.Value,
            new FirebirdSalesService(factory, userContext, clock),
            saleRepository,
            stockLedgers);
    }

    private sealed record TestContext(
        FirebirdTestDatabase Database,
        RetailSetupDefaults Defaults,
        ProductId RiceId,
        ProductId OilId,
        FirebirdSalesService Sales,
        ISaleRepository SaleRepository,
        IStockLedgerRepository StockLedgers);

    private sealed record TestUserContext(Guid UserId) : IUserContext;

    private sealed record TestClock(DateTimeOffset UtcNow) : IClock;

    /// <summary>
    /// A read-only sale lookup for assertions, using its own short-lived unit
    /// of work per call — Firebird's default isolation would otherwise let a
    /// long-lived read transaction miss commits made afterward by
    /// <see cref="FirebirdSalesService"/>'s own short transactions.
    /// </summary>
    private sealed class SaleReader : ISaleRepository
    {
        private readonly FirebirdConnectionFactory _factory;

        public SaleReader(FirebirdConnectionFactory factory)
        {
            _factory = factory;
        }

        public async Task<Sale?> GetAsync(SaleId saleId, CancellationToken cancellationToken)
        {
            await using var unitOfWork = await FirebirdUnitOfWork
                .CreateAsync(_factory, cancellationToken)
                .ConfigureAwait(false);
            return await new FirebirdSaleRepository(unitOfWork)
                .GetAsync(saleId, cancellationToken)
                .ConfigureAwait(false);
        }

        public Task SaveAsync(Sale sale, CancellationToken cancellationToken)
        {
            throw new NotSupportedException("Test reader is read-only.");
        }
    }

    /// <summary>
    /// A read-only stock-ledger lookup for assertions, using its own
    /// short-lived unit of work per call so it always sees what has actually
    /// been committed by the service under test (not a shared, possibly stale,
    /// in-progress transaction).
    /// </summary>
    private sealed class StockLedgerReader : IStockLedgerRepository
    {
        private readonly FirebirdConnectionFactory _factory;

        public StockLedgerReader(FirebirdConnectionFactory factory)
        {
            _factory = factory;
        }

        public async Task<Domain.Inventory.StockLedger?> GetAsync(
            ProductId productId,
            Domain.Inventory.WarehouseId warehouseId,
            CancellationToken cancellationToken)
        {
            await using var unitOfWork = await FirebirdUnitOfWork
                .CreateAsync(_factory, cancellationToken)
                .ConfigureAwait(false);
            return await new FirebirdStockLedgerRepository(unitOfWork)
                .GetAsync(productId, warehouseId, cancellationToken)
                .ConfigureAwait(false);
        }

        public Task SaveAsync(Domain.Inventory.StockLedger ledger, CancellationToken cancellationToken)
        {
            throw new NotSupportedException("Test reader is read-only.");
        }
    }
}
