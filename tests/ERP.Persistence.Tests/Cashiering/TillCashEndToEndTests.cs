using ERP.Application.Cashiering;
using ERP.Application.Catalog;
using ERP.Application.Common;
using ERP.Application.Customers;
using ERP.Application.Inventory;
using ERP.Application.Sales;
using ERP.Domain.Catalog;
using ERP.Domain.Common;
using ERP.Domain.Customers;
using ERP.Domain.Sales;
using ERP.Persistence.Cashiering;
using ERP.Persistence.Customers;
using ERP.Persistence.Database;
using ERP.Persistence.Migrations;
using ERP.Persistence.Sales;
using ERP.Persistence.Services;
using ERP.Persistence.Tests.TestSupport;
using FirebirdSql.Data.FirebirdClient;

namespace ERP.Persistence.Tests.Cashiering;

/// <summary>
/// Regression tests for the two till defects the audit of 1405/07/02 proved on a real Firebird:
/// cash «دریافت از مشتری» never reached the drawer's expected figure, and correcting an earlier
/// shift's cash invoice counted the whole correction as a fresh cash sale.
/// </summary>
public sealed class TillCashEndToEndTests
{
    [Fact]
    public async Task CashTakenFromACustomerAtTheTillIsExpectedInTheDrawer()
    {
        await using var database = FirebirdTestDatabase.Create();
        var c = await SetupAsync(database);
        await c.Till.ExecuteAsync(new OpenCashShiftCommand(c.Defaults.MainWarehouseId, 1_000_000), CancellationToken.None);
        Step(c);

        await CashSaleAsync(c, 2); // 490,000
        Step(c);
        var paid = await c.Customers.ExecuteAsync(
            new RecordCustomerPaymentCommand(c.CustomerId, 3_000_000, CustomerPaymentMethod.Cash, null, c.Defaults.MainWarehouseId),
            CancellationToken.None);
        Assert.True(paid.IsSuccess);
        await c.Customers.ExecuteAsync(
            new RecordCustomerPaymentCommand(c.CustomerId, 1_000_000, CustomerPaymentMethod.Card, null, c.Defaults.MainWarehouseId),
            CancellationToken.None); // card: never in the drawer
        Step(c);

        var status = await c.Till.ExecuteAsync(new GetCashShiftStatusQuery(c.Defaults.MainWarehouseId), CancellationToken.None);

        Assert.Equal(300_000, status.CashReceiptsSoFar!.Value.ToTomansExact());
        Assert.Equal(1_790_000, status.ExpectedCashSoFar!.Value.ToTomansExact()); // ۱٬۰۰۰٬۰۰۰ + ۴۹۰٬۰۰۰ + ۳۰۰٬۰۰۰
    }

    [Fact]
    public async Task CorrectingAnEarlierShiftsCashInvoiceCountsOnlyTheCashItHandsBack()
    {
        await using var database = FirebirdTestDatabase.Create();
        var c = await SetupAsync(database);
        var first = await c.Till.ExecuteAsync(new OpenCashShiftCommand(c.Defaults.MainWarehouseId, 0), CancellationToken.None);
        Step(c);
        var sale = await CashSaleAsync(c, 2); // 490,000 cash, in shift 1
        Step(c);
        var closedFirst = await c.Till.ExecuteAsync(new CloseCashShiftCommand(first.Value, 490_000, null), CancellationToken.None);
        Assert.Equal(0, closedFirst.Value!.VarianceRials);

        c.Clock.UtcNow = c.Clock.UtcNow.AddHours(12);
        var second = await c.Till.ExecuteAsync(new OpenCashShiftCommand(c.Defaults.MainWarehouseId, 100_000), CancellationToken.None);
        Step(c);
        var correction = await c.Sales.ExecuteAsync(new StartSaleCorrectionCommand(sale, "تخفیف فراموش شد"), CancellationToken.None);
        await c.Sales.ExecuteAsync(new SetSaleChargesCommand(correction.Value, 400_000, 0), CancellationToken.None); // 40,000 off
        var corrected = await c.Sales.ExecuteAsync(
            new CompleteSaleCorrectionCommand(correction.Value, PaymentMethod.Cash, 0), CancellationToken.None);
        Assert.True(corrected.IsSuccess, corrected.Error?.Message);
        Step(c);

        var status = await c.Till.ExecuteAsync(new GetCashShiftStatusQuery(c.Defaults.MainWarehouseId), CancellationToken.None);
        Assert.Equal(0, status.CashSalesSoFar!.Value.Rials);
        Assert.Equal(40_000, status.CashRefundsSoFar!.Value.ToTomansExact());
        Assert.Equal(60_000, status.ExpectedCashSoFar!.Value.ToTomansExact()); // ۱۰۰٬۰۰۰ − ۴۰٬۰۰۰ برگشتی

        var closedSecond = await c.Till.ExecuteAsync(new CloseCashShiftCommand(second.Value, 60_000, null), CancellationToken.None);
        Assert.Equal(0, closedSecond.Value!.VarianceRials);
    }

    [Fact]
    public async Task ACorrectionInsideTheSameShiftStillLeavesOnlyTheCorrectedAmount()
    {
        await using var database = FirebirdTestDatabase.Create();
        var c = await SetupAsync(database);
        await c.Till.ExecuteAsync(new OpenCashShiftCommand(c.Defaults.MainWarehouseId, 0), CancellationToken.None);
        Step(c);
        var sale = await CashSaleAsync(c, 2); // 490,000 cash
        Step(c);
        var correction = await c.Sales.ExecuteAsync(new StartSaleCorrectionCommand(sale, "روش پرداخت"), CancellationToken.None);
        await c.Sales.ExecuteAsync(new CompleteSaleCorrectionCommand(correction.Value, PaymentMethod.Card, 0), CancellationToken.None);
        Step(c);

        var status = await c.Till.ExecuteAsync(new GetCashShiftStatusQuery(c.Defaults.MainWarehouseId), CancellationToken.None);

        // the cash was paid and then moved to the card terminal: nothing stays in the drawer
        Assert.Equal(0, status.ExpectedCashSoFar!.Value.Rials);
    }

    [Fact]
    public async Task PaymentsFromBeforeTheUpgradeBelongToTheMainTill()
    {
        await using var database = FirebirdTestDatabase.Create();
        var factory = new FirebirdConnectionFactory(database.Options);
        var all = FirebirdDatabaseBootstrapper.Migrations;
        await new MigrationRunner(factory, all.Where(migration => migration.Version <= 23).ToArray()).MigrateAsync(CancellationToken.None);
        await using (var connection = await factory.OpenAsync(CancellationToken.None))
        {
            foreach (var sql in new[]
            {
                "INSERT INTO CUSTOMER (ID, NAME, MOBILE, STATUS, CODE) VALUES ('cccccccc-cccc-4ccc-8ccc-cccccccccccc', 'علی', '09121234567', 1, 1)",
                """
                INSERT INTO CUSTOMER_PAYMENT (ID, CUSTOMER_ID, AMOUNT_RIALS, METHOD, RECEIVED_AT_UTC)
                VALUES ('dddddddd-dddd-4ddd-8ddd-dddddddddddd', 'cccccccc-cccc-4ccc-8ccc-cccccccccccc', 1000, 1, CURRENT_TIMESTAMP)
                """,
            })
            {
                await using var insert = new FbCommand(sql, connection);
                await insert.ExecuteNonQueryAsync(CancellationToken.None);
            }
        }

        await new MigrationRunner(factory, all).MigrateAsync(CancellationToken.None);

        await using var read = await factory.OpenAsync(CancellationToken.None);
        await using var select = new FbCommand("SELECT WAREHOUSE_ID FROM CUSTOMER_PAYMENT", read);
        Assert.Equal("33333333-3333-4333-8333-333333333333", await select.ExecuteScalarAsync(CancellationToken.None));
    }

    private static void Step(Context c) => c.Clock.UtcNow = c.Clock.UtcNow.AddMinutes(5);

    private static async Task<SaleId> CashSaleAsync(Context c, decimal quantity)
    {
        var sale = (await c.Sales.ExecuteAsync(new StartSaleCommand(c.Defaults.MainWarehouseId), CancellationToken.None)).Value;
        await c.Sales.ExecuteAsync(new AddSaleLineCommand(sale, c.RiceId, quantity), CancellationToken.None);
        var done = await c.Sales.ExecuteAsync(new CompleteSaleCommand(sale, PaymentMethod.Cash, 0, 0), CancellationToken.None);
        Assert.True(done.IsSuccess, done.Error?.Message);
        return sale;
    }

    private static async Task<Context> SetupAsync(FirebirdTestDatabase database)
    {
        var factory = new FirebirdConnectionFactory(database.Options);
        var defaults = await new FirebirdDatabaseBootstrapper(factory).InitializeAsync(CancellationToken.None);
        var user = new TestUserContext(Guid.NewGuid());
        var clock = new TestClock { UtcNow = new DateTimeOffset(2026, 9, 24, 5, 0, 0, TimeSpan.Zero) };
        var setup = new FirebirdRetailSetupService(factory, user, clock, AllowAllAccess.Instance);
        var category = await setup.ExecuteAsync(new CreateCategoryCommand("مواد غذایی", null, 1), CancellationToken.None);
        var rice = await setup.ExecuteAsync(
            new CreateProductCommand("برنج", "RICE-1", category.Value, defaults.EachUnitId, Money.FromTomans(245_000), []),
            CancellationToken.None);
        await setup.ExecuteAsync(
            new ReceiveOpeningStockCommand(rice.Value, defaults.MainWarehouseId, 20, 2_000_000, new DateOnly(2026, 9, 1)),
            CancellationToken.None);
        var customers = new FirebirdCustomerService(factory, user, clock, AllowAllAccess.Instance);
        var customer = await customers.ExecuteAsync(new QuickCreateCustomerCommand("علی رضایی", "09121234567"), CancellationToken.None);
        return new Context(defaults, clock, rice.Value, customer.Value,
            new FirebirdSalesService(factory, user, clock, AllowAllAccess.Instance), customers, new FirebirdCashieringService(factory, user, clock));
    }

    private sealed record Context(
        RetailSetupDefaults Defaults,
        TestClock Clock,
        ProductId RiceId,
        CustomerId CustomerId,
        FirebirdSalesService Sales,
        FirebirdCustomerService Customers,
        FirebirdCashieringService Till);

    private sealed record TestUserContext(Guid UserId) : IUserContext;

    private sealed class TestClock : IClock
    {
        public required DateTimeOffset UtcNow { get; set; }
    }
}
