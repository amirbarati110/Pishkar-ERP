using ERP.Application.Cashiering;
using ERP.Application.Catalog;
using ERP.Application.Common;
using ERP.Application.Customers;
using ERP.Application.Inventory;
using ERP.Application.Sales;
using ERP.Domain.Catalog;
using ERP.Domain.Common;
using ERP.Domain.Sales;
using ERP.Persistence.Cashiering;
using ERP.Persistence.Customers;
using ERP.Persistence.Database;
using ERP.Persistence.Migrations;
using ERP.Persistence.Sales;
using ERP.Persistence.Services;
using ERP.Persistence.Tests.TestSupport;
using FirebirdSql.Data.FirebirdClient;

namespace ERP.Persistence.Tests.Sales;

/// <summary>
/// «خدمات/هزینه هم پس داده شود» on a real Firebird: every place that adds returns up — the
/// customer's balance, the customer list, the till — must count the service charge, including a
/// return that holds nothing else.
/// </summary>
public sealed class ServiceChargeReturnEndToEndTests
{
    [Fact]
    public async Task AServiceOnlyReturnReachesTheCustomerBalanceAndTheTill()
    {
        await using var database = FirebirdTestDatabase.Create();
        var c = await SetupAsync(database);

        // credit sale: 2 × 245,000 + 20,000 delivery, 10% VAT = 561,000 toman
        var credit = await SaleAsync(c, PaymentMethod.Credit, deliveryRials: 200_000);
        Assert.Equal(5_610_000, credit.Total);
        var refundDelivery = await c.Sales.ExecuteAsync(
            new CompleteSaleReturnCommand(credit.Id, [], PaymentMethod.Credit, "ارسال انجام نشد", RefundServiceCharge: true),
            CancellationToken.None);
        Assert.True(refundDelivery.IsSuccess, refundDelivery.Error?.Message);
        Assert.Equal(220_000, refundDelivery.Value!.Refund.Rials);

        var account = await c.Customers.ExecuteAsync(new GetCustomerAccountQuery(c.CustomerId), CancellationToken.None);
        Assert.Equal(5_390_000, account.Value!.Debt.Rials);
        var listed = await c.Customers.ExecuteAsync(new CustomerListQuery(null, false), CancellationToken.None);
        Assert.Equal(5_390_000, Assert.Single(listed.Rows).Debt.Rials);

        // cash sale in an open shift, delivery given back in cash: the drawer pays it out
        await c.Till.ExecuteAsync(new OpenCashShiftCommand(c.Defaults.MainWarehouseId, 100_000), CancellationToken.None);
        c.Clock.UtcNow = c.Clock.UtcNow.AddMinutes(1);
        var cash = await SaleAsync(c, PaymentMethod.Cash, deliveryRials: 200_000, withCustomer: false);
        await c.Sales.ExecuteAsync(
            new CompleteSaleReturnCommand(cash.Id, [], PaymentMethod.Cash, "ارسال انجام نشد", RefundServiceCharge: true),
            CancellationToken.None);
        c.Clock.UtcNow = c.Clock.UtcNow.AddMinutes(1);

        var status = await c.Till.ExecuteAsync(new GetCashShiftStatusQuery(c.Defaults.MainWarehouseId), CancellationToken.None);
        Assert.Equal(22_000, status.CashRefundsSoFar!.Value.ToTomansExact());
        Assert.Equal(100_000 + 561_000 - 22_000, status.ExpectedCashSoFar!.Value.ToTomansExact());
    }

    [Fact]
    public async Task TheReturnWindowOffersTheServiceChargeUntilItIsGivenBackThenNeverAgain()
    {
        await using var database = FirebirdTestDatabase.Create();
        var c = await SetupAsync(database);
        var sale = await SaleAsync(c, PaymentMethod.Credit, deliveryRials: 200_000);

        var before = await c.Sales.ExecuteAsync(new GetReturnableSaleQuery(sale.Id), CancellationToken.None);
        Assert.Equal(220_000, before.Value!.RefundableServiceCharge.Total.Rials);

        await c.Sales.ExecuteAsync(
            new CompleteSaleReturnCommand(sale.Id, [new ReturnLineRequest(c.RiceId, 1, ReturnDisposition.ToStock)], PaymentMethod.Credit,
                "یکی خراب", RefundServiceCharge: true),
            CancellationToken.None);
        var after = await c.Sales.ExecuteAsync(new GetReturnableSaleQuery(sale.Id), CancellationToken.None);
        var again = await c.Sales.ExecuteAsync(
            new CompleteSaleReturnCommand(sale.Id, [], PaymentMethod.Credit, "دوباره", RefundServiceCharge: true),
            CancellationToken.None);

        Assert.True(after.Value!.RefundableServiceCharge.IsEmpty);
        Assert.False(again.IsSuccess);
    }

    [Fact]
    public async Task ReturnsMadeBeforeTheUpgradeRefundedNoServiceCharge()
    {
        await using var database = FirebirdTestDatabase.Create();
        var factory = new FirebirdConnectionFactory(database.Options);
        var all = FirebirdDatabaseBootstrapper.Migrations;
        await new MigrationRunner(factory, all.Where(migration => migration.Version <= 22).ToArray()).MigrateAsync(CancellationToken.None);

        await using (var connection = await factory.OpenAsync(CancellationToken.None))
        {
            foreach (var sql in new[]
            {
                """
                INSERT INTO SALE (ID, WAREHOUSE_ID, OPENED_AT_UTC, STATUS, DISCOUNT_RIALS)
                VALUES ('aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', '33333333-3333-4333-8333-333333333333', CURRENT_TIMESTAMP, 1, 0)
                """,
                """
                INSERT INTO SALE_RETURN (ID, SALE_ID, WAREHOUSE_ID, NUMBER, REFUND_METHOD, REASON, COMPLETED_AT_UTC)
                VALUES ('bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb', 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
                        '33333333-3333-4333-8333-333333333333', 1, 1, 'old', CURRENT_TIMESTAMP)
                """,
            })
            {
                await using var insert = new FbCommand(sql, connection);
                await insert.ExecuteNonQueryAsync(CancellationToken.None);
            }
        }

        await new MigrationRunner(factory, all).MigrateAsync(CancellationToken.None);

        await using var read = await factory.OpenAsync(CancellationToken.None);
        await using var select = new FbCommand("SELECT SERVICE_NET_RIALS, SERVICE_TAX_RIALS FROM SALE_RETURN", read);
        await using var reader = await select.ExecuteReaderAsync(CancellationToken.None);
        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.Equal(0, reader.GetInt64(0));
        Assert.Equal(0, reader.GetInt64(1));
    }

    private static async Task<(SaleId Id, long Total)> SaleAsync(
        Context c, PaymentMethod method, long deliveryRials, bool withCustomer = true)
    {
        var sale = (await c.Sales.ExecuteAsync(new StartSaleCommand(c.Defaults.MainWarehouseId), CancellationToken.None)).Value;
        await c.Sales.ExecuteAsync(new AddSaleLineCommand(sale, c.RiceId, 2), CancellationToken.None);
        if (withCustomer)
        {
            await c.Sales.ExecuteAsync(new SetSaleCustomerCommand(sale, c.CustomerId), CancellationToken.None);
        }

        await c.Sales.ExecuteAsync(new SetSaleChargesCommand(sale, 0, deliveryRials), CancellationToken.None);
        var done = await c.Sales.ExecuteAsync(
            new CompleteSaleCommand(sale, method, 0, 10, ApproveCreditOverLimit: true), CancellationToken.None);
        Assert.True(done.IsSuccess, done.Error?.Message);
        return (sale, done.Value!.Totals.Total.Rials);
    }

    private static async Task<Context> SetupAsync(FirebirdTestDatabase database)
    {
        var factory = new FirebirdConnectionFactory(database.Options);
        var defaults = await new FirebirdDatabaseBootstrapper(factory).InitializeAsync(CancellationToken.None);
        var user = new TestUserContext(Guid.NewGuid());
        var clock = new TestClock { UtcNow = new DateTimeOffset(2026, 9, 24, 6, 0, 0, TimeSpan.Zero) };
        var setup = new FirebirdRetailSetupService(factory, user, clock);
        var category = await setup.ExecuteAsync(new CreateCategoryCommand("مواد غذایی", null, 1), CancellationToken.None);
        var rice = await setup.ExecuteAsync(
            new CreateProductCommand("برنج", "RICE-1", category.Value, defaults.EachUnitId, Money.FromTomans(245_000), []),
            CancellationToken.None);
        await setup.ExecuteAsync(
            new ReceiveOpeningStockCommand(rice.Value, defaults.MainWarehouseId, 20, 2_000_000, new DateOnly(2026, 9, 1)),
            CancellationToken.None);
        var customers = new FirebirdCustomerService(factory, user, clock);
        var customer = await customers.ExecuteAsync(new QuickCreateCustomerCommand("علی رضایی", "09121234567"), CancellationToken.None);
        return new Context(defaults, clock, rice.Value, customer.Value,
            new FirebirdSalesService(factory, user, clock), customers, new FirebirdCashieringService(factory, user, clock));
    }

    private sealed record Context(
        RetailSetupDefaults Defaults,
        TestClock Clock,
        ProductId RiceId,
        ERP.Domain.Customers.CustomerId CustomerId,
        FirebirdSalesService Sales,
        FirebirdCustomerService Customers,
        FirebirdCashieringService Till);

    private sealed record TestUserContext(Guid UserId) : IUserContext;

    private sealed class TestClock : IClock
    {
        public required DateTimeOffset UtcNow { get; set; }
    }
}
