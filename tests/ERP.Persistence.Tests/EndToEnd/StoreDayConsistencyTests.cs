using System.Globalization;
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
using ERP.Persistence.Inventory;
using ERP.Persistence.Sales;
using ERP.Persistence.Services;
using ERP.Persistence.Tests.TestSupport;
using FirebirdSql.Data.FirebirdClient;
using Xunit.Abstractions;

namespace ERP.Persistence.Tests.EndToEnd;

/// <summary>
/// One realistic store day on a real Firebird, then every module's figures cross-checked against
/// each other: stock ↔ kardex ↔ journal ↔ customer balance ↔ till ↔ day report. Born as the audit
/// probe of 1405/07/02, which found four of these disagreeing (customer payments and opening stock
/// never reached the journal, customer cash never reached the till); kept so they cannot drift again.
/// </summary>
public sealed class StoreDayConsistencyTests
{
    private readonly ITestOutputHelper _output;

    public StoreDayConsistencyTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task EveryModuleTellsTheSameStoryAfterADay()
    {
        await using var database = FirebirdTestDatabase.Create();
        var factory = new FirebirdConnectionFactory(database.Options);
        var defaults = await new FirebirdDatabaseBootstrapper(factory).InitializeAsync(CancellationToken.None);
        var user = new TestUserContext(Guid.NewGuid());
        var clock = new TestClock { UtcNow = new DateTimeOffset(2026, 9, 17, 5, 0, 0, TimeSpan.Zero) }; // 08:30 Tehran
        var setup = new FirebirdRetailSetupService(factory, user, clock, AllowAllAccess.Instance);
        var sales = new FirebirdSalesService(factory, user, clock, AllowAllAccess.Instance);
        var till = new FirebirdCashieringService(factory, user, clock);
        var customers = new FirebirdCustomerService(factory, user, clock, AllowAllAccess.Instance);
        var w = defaults.MainWarehouseId;

        var category = await setup.ExecuteAsync(new CreateCategoryCommand("مواد غذایی", null, 1), CancellationToken.None);
        var rice = (await setup.ExecuteAsync(new CreateProductCommand("برنج", "RICE", category.Value, defaults.EachUnitId, Money.FromTomans(245_000), []), CancellationToken.None)).Value;
        var oil = (await setup.ExecuteAsync(new CreateProductCommand("روغن", "OIL", category.Value, defaults.EachUnitId, Money.FromTomans(100_000), []), CancellationToken.None)).Value;
        await setup.ExecuteAsync(new ReceiveOpeningStockCommand(rice, w, 20, 2_000_000, new DateOnly(2026, 9, 1)), CancellationToken.None);
        await setup.ExecuteAsync(new ReceiveOpeningStockCommand(oil, w, 10, 800_000, new DateOnly(2026, 9, 1)), CancellationToken.None);
        var ali = (await customers.ExecuteAsync(new QuickCreateCustomerCommand("علی رضایی", "09121234567"), CancellationToken.None)).Value;

        Step(clock);
        var shift = await till.ExecuteAsync(new OpenCashShiftCommand(w, 1_000_000), CancellationToken.None);
        Ok(shift.IsSuccess, "open shift", shift.Error?.Message);

        // A: cash, 2 rice = 4,900,000 rials
        Step(clock);
        var a = (await sales.ExecuteAsync(new StartSaleCommand(w), CancellationToken.None)).Value;
        await sales.ExecuteAsync(new AddSaleLineCommand(a, rice, 2), CancellationToken.None);
        Ok((await sales.ExecuteAsync(new CompleteSaleCommand(a, PaymentMethod.Cash, 0, 0), CancellationToken.None)).IsSuccess, "sale A");

        // B: card, 1 oil + 200,000 service, 10% VAT = 1,320,000
        Step(clock);
        var b = (await sales.ExecuteAsync(new StartSaleCommand(w), CancellationToken.None)).Value;
        await sales.ExecuteAsync(new AddSaleLineCommand(b, oil, 1), CancellationToken.None);
        await sales.ExecuteAsync(new SetSaleChargesCommand(b, 0, 200_000), CancellationToken.None);
        Ok((await sales.ExecuteAsync(new CompleteSaleCommand(b, PaymentMethod.Card, 0, 10), CancellationToken.None)).IsSuccess, "sale B");

        // C: credit to Ali, 3 rice − 350,000 discount = 7,000,000
        Step(clock);
        var c = (await sales.ExecuteAsync(new StartSaleCommand(w), CancellationToken.None)).Value;
        await sales.ExecuteAsync(new AddSaleLineCommand(c, rice, 3), CancellationToken.None);
        await sales.ExecuteAsync(new SetSaleCustomerCommand(c, ali), CancellationToken.None);
        await sales.ExecuteAsync(new SetSaleChargesCommand(c, 350_000, 0), CancellationToken.None);
        var saleC = await sales.ExecuteAsync(new CompleteSaleCommand(c, PaymentMethod.Credit, 0, 0, ApproveCreditOverLimit: true), CancellationToken.None);
        Ok(saleC.IsSuccess, "sale C", saleC.Error?.Message);

        // Ali pays 3,000,000 in cash at the till
        Step(clock);
        var paid = await customers.ExecuteAsync(new RecordCustomerPaymentCommand(ali, 3_000_000, CustomerPaymentMethod.Cash, null, w), CancellationToken.None);
        Ok(paid.IsSuccess, "customer payment", paid.Error?.Message);

        // Return 1 rice from A back to stock, cash refund 2,450,000
        Step(clock);
        var ret = await sales.ExecuteAsync(
            new CompleteSaleReturnCommand(a, [new ReturnLineRequest(rice, 1, ReturnDisposition.ToStock)], PaymentMethod.Cash, "خراب نبود"),
            CancellationToken.None);
        Ok(ret.IsSuccess, "return", ret.Error?.Message);
        Step(clock);

        // ───── cross-checks (all in rials) ─────
        var cards = new GetStockCardHandler(new FirebirdStockCardReader(factory));
        var riceCard = await cards.ExecuteAsync(new StockCardQuery(rice), CancellationToken.None);
        var oilCard = await cards.ExecuteAsync(new StockCardQuery(oil), CancellationToken.None);
        Check("stock rice qty", 16m, riceCard!.ClosingBalance);
        Check("stock oil qty", 9m, oilCard!.ClosingBalance);
        Check("kardex rice value", 32_000_000L, riceCard.ClosingValue?.Rials);
        Check("kardex oil value", 7_200_000L, oilCard.ClosingValue?.Rials);

        var debits = await ScalarAsync(factory, "SELECT CAST(SUM(DEBIT_RIALS) AS BIGINT) FROM JOURNAL_LINE");
        var credits = await ScalarAsync(factory, "SELECT CAST(SUM(CREDIT_RIALS) AS BIGINT) FROM JOURNAL_LINE");
        Check("journal balanced", debits, credits);
        Check("journal Cash (sales − refunds + customer cash)", 5_450_000L, await AccountAsync(factory, 1));
        Check("journal Card clearing", 1_320_000L, await AccountAsync(factory, 2));
        Check("journal Receivable = customer debt", 4_000_000L, await AccountAsync(factory, 3));
        Check("journal net revenue (5) − returns (9)", 10_650_000L, -(await AccountAsync(factory, 5)) - (await AccountAsync(factory, 9)));
        Check("journal VAT payable", 120_000L, -(await AccountAsync(factory, 6)));
        Check("journal COGS", 8_800_000L, await AccountAsync(factory, 7));
        Check("journal Inventory = stock value", 39_200_000L, await AccountAsync(factory, 8));
        Check("journal opening equity = stock brought in", -48_000_000L, await AccountAsync(factory, 10));

        var account = await customers.ExecuteAsync(new GetCustomerAccountQuery(ali), CancellationToken.None);
        Check("customer debt", 4_000_000L, account.Value?.Debt.Rials);

        var status = await till.ExecuteAsync(new GetCashShiftStatusQuery(w), CancellationToken.None);
        Check("till expected cash (opening + cash sales + customer cash − refunds)", 15_450_000L, status.ExpectedCashSoFar?.Rials);

        var day = await sales.ExecuteAsync(new ListSalesOfDayQuery(PersianDate.FromUtc(clock.UtcNow)), CancellationToken.None);
        Check("day total (invoices)", 13_220_000L, day.Total.Rials);
        Check("day cash", 4_900_000L, day.TotalsByPaymentMethod.GetValueOrDefault(PaymentMethod.Cash).Rials);
        Check("day credit", 7_000_000L, day.TotalsByPaymentMethod.GetValueOrDefault(PaymentMethod.Credit).Rials);

        foreach (var line in _log)
        {
            _output.WriteLine(line);
        }

        Assert.True(_mismatches.Count == 0, "MISMATCHES:\n" + string.Join("\n", _mismatches));
    }

    [Fact]
    public async Task ReturningACreditTaxedDiscountedInvoicePieceByPieceStaysConsistent()
    {
        await using var database = FirebirdTestDatabase.Create();
        var factory = new FirebirdConnectionFactory(database.Options);
        var defaults = await new FirebirdDatabaseBootstrapper(factory).InitializeAsync(CancellationToken.None);
        var user = new TestUserContext(Guid.NewGuid());
        var clock = new TestClock { UtcNow = new DateTimeOffset(2026, 9, 17, 5, 0, 0, TimeSpan.Zero) };
        var setup = new FirebirdRetailSetupService(factory, user, clock, AllowAllAccess.Instance);
        var sales = new FirebirdSalesService(factory, user, clock, AllowAllAccess.Instance);
        var customers = new FirebirdCustomerService(factory, user, clock, AllowAllAccess.Instance);
        var w = defaults.MainWarehouseId;
        var category = await setup.ExecuteAsync(new CreateCategoryCommand("مواد غذایی", null, 1), CancellationToken.None);
        var rice = (await setup.ExecuteAsync(new CreateProductCommand("برنج", "RICE", category.Value, defaults.EachUnitId, Money.FromTomans(245_000), []), CancellationToken.None)).Value;
        await setup.ExecuteAsync(new ReceiveOpeningStockCommand(rice, w, 20, 2_000_000, new DateOnly(2026, 9, 1)), CancellationToken.None);
        var ali = (await customers.ExecuteAsync(new QuickCreateCustomerCommand("علی رضایی", "09121234567"), CancellationToken.None)).Value;

        // 3 rice, 350,000 discount, 70,000 service, 10% VAT, on credit
        Step(clock);
        var c = (await sales.ExecuteAsync(new StartSaleCommand(w), CancellationToken.None)).Value;
        await sales.ExecuteAsync(new AddSaleLineCommand(c, rice, 3), CancellationToken.None);
        await sales.ExecuteAsync(new SetSaleCustomerCommand(c, ali), CancellationToken.None);
        await sales.ExecuteAsync(new SetSaleChargesCommand(c, 350_000, 70_000), CancellationToken.None);
        var sold = await sales.ExecuteAsync(new CompleteSaleCommand(c, PaymentMethod.Credit, 0, 10, ApproveCreditOverLimit: true), CancellationToken.None);
        Ok(sold.IsSuccess, "sale", sold.Error?.Message);
        var total = sold.Value!.Totals.Total.Rials; // (7,350,000 − 350,000 + 70,000) × 1.1 = 7,777,000
        _log.Add($"sale total {total}, tax {sold.Value.Totals.Tax.Rials}");

        long refunded = 0;
        for (var i = 0; i < 3; i++)
        {
            Step(clock);
            var r = await sales.ExecuteAsync(
                new CompleteSaleReturnCommand(c, [new ReturnLineRequest(rice, 1, ReturnDisposition.ToStock)], PaymentMethod.Credit, "مشتری پس آورد"),
                CancellationToken.None);
            Ok(r.IsSuccess, $"return #{i + 1}", r.Error?.Message);
            refunded += r.Value?.Refund.Rials ?? 0;
            _log.Add($"return #{i + 1}: refund {r.Value?.Refund.Rials}");
        }

        var debt = (await customers.ExecuteAsync(new GetCustomerAccountQuery(ali), CancellationToken.None)).Value!;
        Check("goods fully returned: refunds + service part = total", total, refunded + 77_000);
        Check("customer debt = service charge and its VAT only", 77_000L, debt.Debt.Rials);
        Check("journal Receivable = customer debt", debt.Debt.Rials, await AccountAsync(factory, 3));
        Check("journal VAT = VAT on the service charge only", 7_000L, -(await AccountAsync(factory, 6)));
        Check("journal Inventory back to the opening stock's value", 40_000_000L, await AccountAsync(factory, 8));
        Check("journal COGS back to 0", 0L, await AccountAsync(factory, 7));

        foreach (var line in _log)
        {
            _output.WriteLine(line);
        }

        Assert.True(_mismatches.Count == 0, "MISMATCHES:\n" + string.Join("\n", _mismatches));
    }

    [Fact]
    public async Task ACorrectedInvoicesRevenueAndMoneyAreCountedOnceInTheJournal()
    {
        await using var database = FirebirdTestDatabase.Create();
        var factory = new FirebirdConnectionFactory(database.Options);
        var defaults = await new FirebirdDatabaseBootstrapper(factory).InitializeAsync(CancellationToken.None);
        var user = new TestUserContext(Guid.NewGuid());
        var clock = new TestClock { UtcNow = new DateTimeOffset(2026, 9, 17, 5, 0, 0, TimeSpan.Zero) };
        var setup = new FirebirdRetailSetupService(factory, user, clock, AllowAllAccess.Instance);
        var sales = new FirebirdSalesService(factory, user, clock, AllowAllAccess.Instance);
        var category = await setup.ExecuteAsync(new CreateCategoryCommand("مواد غذایی", null, 1), CancellationToken.None);
        var rice = (await setup.ExecuteAsync(new CreateProductCommand("برنج", "RICE", category.Value, defaults.EachUnitId, Money.FromTomans(245_000), []), CancellationToken.None)).Value;
        await setup.ExecuteAsync(new ReceiveOpeningStockCommand(rice, defaults.MainWarehouseId, 20, 2_000_000, new DateOnly(2026, 9, 1)), CancellationToken.None);

        Step(clock);
        var sale = (await sales.ExecuteAsync(new StartSaleCommand(defaults.MainWarehouseId), CancellationToken.None)).Value;
        await sales.ExecuteAsync(new AddSaleLineCommand(sale, rice, 2), CancellationToken.None);
        await sales.ExecuteAsync(new CompleteSaleCommand(sale, PaymentMethod.Cash, 0, 0), CancellationToken.None); // 4,900,000
        Step(clock);
        var correction = await sales.ExecuteAsync(new StartSaleCorrectionCommand(sale, "روش پرداخت"), CancellationToken.None);
        var corrected = await sales.ExecuteAsync(new CompleteSaleCorrectionCommand(correction.Value, PaymentMethod.Card, 0), CancellationToken.None);
        Ok(corrected.IsSuccess, "correction", corrected.Error?.Message);

        Check("journal revenue counted once", 4_900_000L, -(await AccountAsync(factory, 5)));
        Check("journal cash: paid then moved to the card", 0L, await AccountAsync(factory, 1));
        Check("journal card clearing", 4_900_000L, await AccountAsync(factory, 2));
        Check("journal COGS unchanged by the correction", 4_000_000L, await AccountAsync(factory, 7));

        foreach (var line in _log)
        {
            _output.WriteLine(line);
        }

        Assert.True(_mismatches.Count == 0, "MISMATCHES:\n" + string.Join("\n", _mismatches));
    }

    private readonly List<string> _log = [];
    private readonly List<string> _mismatches = [];

    private void Ok(bool success, string what, string? why = null)
    {
        if (!success)
        {
            _mismatches.Add($"{what} failed: {why}");
        }
    }

    private void Check<T>(string what, T expected, T actual)
    {
        var line = string.Create(CultureInfo.InvariantCulture, $"{(Equals(expected, actual) ? "OK  " : "BAD ")} {what}: expected {expected}, actual {actual}");
        _log.Add(line);
        if (!Equals(expected, actual))
        {
            _mismatches.Add(line);
        }
    }

    private static void Step(TestClock clock) => clock.UtcNow = clock.UtcNow.AddMinutes(7);

    private static Task<long> AccountAsync(FirebirdConnectionFactory factory, int account) =>
        ScalarAsync(factory, $"SELECT CAST(COALESCE(SUM(DEBIT_RIALS - CREDIT_RIALS), 0) AS BIGINT) FROM JOURNAL_LINE WHERE ACCOUNT = {account}");

    private static async Task<long> ScalarAsync(FirebirdConnectionFactory factory, string sql)
    {
        await using var connection = await factory.OpenAsync(CancellationToken.None);
        await using var command = new FbCommand(sql, connection);
        var value = await command.ExecuteScalarAsync(CancellationToken.None);
        return value is DBNull or null ? 0 : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    private sealed record TestUserContext(Guid UserId) : IUserContext;

    private sealed class TestClock : IClock
    {
        public required DateTimeOffset UtcNow { get; set; }
    }
}
