using System.Diagnostics;
using ERP.Application.Catalog;
using ERP.Application.Common;
using ERP.Application.Inventory;
using ERP.Application.Sales;
using ERP.Domain.Catalog;
using ERP.Domain.Common;
using ERP.Domain.Sales;
using ERP.Persistence.Catalog;
using ERP.Persistence.Database;
using ERP.Persistence.Sales;
using ERP.Persistence.Services;
using ERP.Persistence.Tests.TestSupport;

namespace ERP.Persistence.Tests.EndToEnd;

/// <summary>
/// §2.8 Performance Budget (سند اصلی): «Query معمول Local: هدف کمتر از 200ms»،
/// «ثبت فاکتور بدون Integration خارجی: هدف کمتر از 500ms». تا پیوست م (§13 #5)
/// این بند صادقانه «ساخته نشد» ثبت شده بود چون نیاز به داده‌ی نمونه‌ی حجیم
/// داشت. این تست همان داده را واقعاً می‌سازد (نه Mock) و زمان واقعی روی
/// Firebird واقعی را اندازه می‌گیرد.
/// </summary>
public sealed class PerformanceBudgetTests
{
    private const int ProductCount = 1_500;
    private const int SaleCount = 800;

    [Fact]
    public async Task DailyQueriesAndLocalSaleStayWithinBudgetUnderRealisticVolume()
    {
        await using var database = FirebirdTestDatabase.Create();
        var factory = new FirebirdConnectionFactory(database.Options);
        var defaults = await new FirebirdDatabaseBootstrapper(factory).InitializeAsync(CancellationToken.None);
        var userContext = new TestUserContext(Guid.NewGuid());
        var clock = new TestClock { UtcNow = new DateTimeOffset(2026, 9, 18, 7, 0, 0, TimeSpan.Zero) };

        var retailSetup = new FirebirdRetailSetupService(factory, userContext, clock, AllowAllAccess.Instance);
        var sales = new FirebirdSalesService(factory, userContext, clock, AllowAllAccess.Instance);

        var category = await retailSetup.ExecuteAsync(
            new CreateCategoryCommand("مواد غذایی", null, 1), CancellationToken.None);
        Assert.True(category.IsSuccess);

        // ۱) داده‌ی نمونه‌ی حجیم: هزار و پانصد کالا، هرکدام با موجودی اولیه‌ی واقعی.
        var productIds = new List<ProductId>(ProductCount);
        for (var i = 0; i < ProductCount; i++)
        {
            var product = await retailSetup.ExecuteAsync(
                new CreateProductCommand(
                    $"کالای نمونه {i:D5}",
                    $"PERF-{i:D5}",
                    category.Value,
                    defaults.EachUnitId,
                    Money.FromTomans(100_000 + i),
                    [$"62990{i:D8}"]),
                CancellationToken.None);
            Assert.True(product.IsSuccess);
            productIds.Add(product.Value);

            var opening = await retailSetup.ExecuteAsync(
                new ReceiveOpeningStockCommand(
                    product.Value, defaults.MainWarehouseId, 1_000, 50_000_0, new DateOnly(2026, 9, 1)),
                CancellationToken.None);
            Assert.True(opening.IsSuccess);
        }

        // ۲) هشتصد فاکتور نمونه‌ی تکمیل‌شده — تاریخچه‌ی فروش واقعی برای «پرفروش‌ها» و بدهی روزانه.
        var random = new Random(42);
        for (var i = 0; i < SaleCount; i++)
        {
            clock.UtcNow = clock.UtcNow.AddMinutes(1);
            var productId = productIds[random.Next(productIds.Count)];

            var started = await sales.ExecuteAsync(new StartSaleCommand(defaults.MainWarehouseId), CancellationToken.None);
            Assert.True(started.IsSuccess);
            var lineAdded = await sales.ExecuteAsync(
                new AddSaleLineCommand(started.Value, productId, 1), CancellationToken.None);
            Assert.True(lineAdded.IsSuccess);
            var completed = await sales.ExecuteAsync(
                new CompleteSaleCommand(started.Value, PaymentMethod.Cash, DiscountRials: 0, TaxRatePercent: 10),
                CancellationToken.None);
            Assert.True(completed.IsSuccess);
        }

        // ۳) کوئری روزمره #۱ — جست‌وجوی کالا: پرتکرارترین عملیات صفحه‌ی فروش (§۶.۸).
        var searchHandler = new SearchProductsHandler(new FirebirdProductSearchReader(factory));
        var searchStopwatch = Stopwatch.StartNew();
        var searchResult = await searchHandler.ExecuteAsync(
            new SearchProductsQuery("کالای نمونه"), CancellationToken.None);
        searchStopwatch.Stop();
        Assert.NotEmpty(searchResult);
        Assert.True(
            searchStopwatch.ElapsedMilliseconds < 200,
            $"جست‌وجوی کالا روی {ProductCount} کالا {searchStopwatch.ElapsedMilliseconds}ms طول کشید؛ هدف سند (§۲.۸) زیر ۲۰۰ms است.");

        // ۴) کوئری روزمره #۲ — فهرست «همه کالاها»ی صفحه‌ی فروش، صفحه‌بندی‌شده از دیتابیس.
        var browseStopwatch = Stopwatch.StartNew();
        var browseResult = await sales.ExecuteAsync(
            new BrowseProductsForSaleQuery(defaults.MainWarehouseId, CategoryId: null, ProductListFilter.All),
            CancellationToken.None);
        browseStopwatch.Stop();
        Assert.NotEmpty(browseResult.Items);
        Assert.True(
            browseStopwatch.ElapsedMilliseconds < 200,
            $"فهرست کالاهای صفحه‌ی فروش روی {ProductCount} کالا {browseStopwatch.ElapsedMilliseconds}ms طول کشید؛ هدف سند (§۲.۸) زیر ۲۰۰ms است.");

        // ۵) ثبت فاکتور محلی — سه مرحله‌ی کامل صفحه‌ی فروش، روی دیتابیسی که دیگر خالی نیست.
        var saleStopwatch = Stopwatch.StartNew();
        var newSale = await sales.ExecuteAsync(new StartSaleCommand(defaults.MainWarehouseId), CancellationToken.None);
        Assert.True(newSale.IsSuccess);
        var newLine = await sales.ExecuteAsync(
            new AddSaleLineCommand(newSale.Value, productIds[0], 1), CancellationToken.None);
        Assert.True(newLine.IsSuccess);
        var newCompleted = await sales.ExecuteAsync(
            new CompleteSaleCommand(newSale.Value, PaymentMethod.Cash, DiscountRials: 0, TaxRatePercent: 10),
            CancellationToken.None);
        saleStopwatch.Stop();
        Assert.True(newCompleted.IsSuccess);
        Assert.True(
            saleStopwatch.ElapsedMilliseconds < 500,
            $"ثبت فاکتور محلی روی دیتابیس پرداده {saleStopwatch.ElapsedMilliseconds}ms طول کشید؛ هدف سند (§۲.۸) زیر ۵۰۰ms است.");
    }

    private sealed record TestUserContext(Guid UserId) : IUserContext;

    private sealed class TestClock : IClock
    {
        public required DateTimeOffset UtcNow { get; set; }
    }
}
