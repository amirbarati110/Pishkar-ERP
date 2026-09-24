using ERP.Application.Catalog;
using ERP.Application.Common;
using ERP.Domain.Common;
using ERP.Persistence.Catalog;
using ERP.Persistence.Database;
using ERP.Persistence.Services;
using ERP.Persistence.Tests.TestSupport;

namespace ERP.Persistence.Tests.Catalog;

public sealed class ProductSearchReaderTests
{
    [Fact]
    public async Task SearchRanksPrefixMatchAboveMidWordMatchAndRespectsMaxResults()
    {
        await using var database = FirebirdTestDatabase.Create();
        var factory = new FirebirdConnectionFactory(database.Options);
        var defaults = await new FirebirdDatabaseBootstrapper(factory)
            .InitializeAsync(CancellationToken.None);
        var setup = new FirebirdRetailSetupService(
            factory,
            new TestUserContext(Guid.NewGuid()),
            new TestClock(new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.Zero)), AllowAllAccess.Instance);
        var category = await setup.ExecuteAsync(
            new CreateCategoryCommand("مواد غذایی", null, 1),
            CancellationToken.None);

        // Cashier types "روغ" (a half word): "روغن حیوانی" starts with it and
        // must be ranked first; "کره و روغن" only contains it mid-string and
        // must still show up, just after the prefix match.
        await setup.ExecuteAsync(
            new CreateProductCommand(
                "روغن حیوانی",
                "OIL-1",
                category.Value,
                defaults.EachUnitId,
                Money.FromTomans(85_000),
                ["6260000001101"]),
            CancellationToken.None);
        await setup.ExecuteAsync(
            new CreateProductCommand(
                "کره و روغن ترکیبی",
                "OIL-2",
                category.Value,
                defaults.EachUnitId,
                Money.FromTomans(64_000),
                ["6260000001102"]),
            CancellationToken.None);
        await setup.ExecuteAsync(
            new CreateProductCommand(
                "برنج ایرانی هاشمی",
                "RICE-1",
                category.Value,
                defaults.EachUnitId,
                Money.FromTomans(245_000),
                ["6260000001103"]),
            CancellationToken.None);

        var reader = new FirebirdProductSearchReader(factory);
        var handler = new SearchProductsHandler(reader);

        var results = await handler.ExecuteAsync(
            new SearchProductsQuery("روغ"),
            CancellationToken.None);

        Assert.Equal(2, results.Count);
        Assert.Equal("روغن حیوانی", results[0].Name);
        Assert.Equal("کره و روغن ترکیبی", results[1].Name);
    }

    [Fact]
    public async Task SearchMatchesByBarcodePrefixForScannerFlow()
    {
        await using var database = FirebirdTestDatabase.Create();
        var factory = new FirebirdConnectionFactory(database.Options);
        var defaults = await new FirebirdDatabaseBootstrapper(factory)
            .InitializeAsync(CancellationToken.None);
        var setup = new FirebirdRetailSetupService(
            factory,
            new TestUserContext(Guid.NewGuid()),
            new TestClock(new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.Zero)), AllowAllAccess.Instance);
        var category = await setup.ExecuteAsync(
            new CreateCategoryCommand("مواد غذایی", null, 1),
            CancellationToken.None);
        await setup.ExecuteAsync(
            new CreateProductCommand(
                "چای ایرانی",
                "TEA-1",
                category.Value,
                defaults.EachUnitId,
                Money.FromTomans(150_000),
                ["6260000002001"]),
            CancellationToken.None);

        var reader = new FirebirdProductSearchReader(factory);
        var handler = new SearchProductsHandler(reader);

        var results = await handler.ExecuteAsync(
            new SearchProductsQuery("626000000200"),
            CancellationToken.None);

        var match = Assert.Single(results);
        Assert.Equal("چای ایرانی", match.Name);
        Assert.Equal("6260000002001", match.PrimaryBarcode);
    }

    [Fact]
    public async Task SearchLimitsResultCountToMaxResults()
    {
        await using var database = FirebirdTestDatabase.Create();
        var factory = new FirebirdConnectionFactory(database.Options);
        var defaults = await new FirebirdDatabaseBootstrapper(factory)
            .InitializeAsync(CancellationToken.None);
        var setup = new FirebirdRetailSetupService(
            factory,
            new TestUserContext(Guid.NewGuid()),
            new TestClock(new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.Zero)), AllowAllAccess.Instance);
        var category = await setup.ExecuteAsync(
            new CreateCategoryCommand("نوشیدنی", null, 1),
            CancellationToken.None);

        for (var index = 1; index <= 5; index++)
        {
            await setup.ExecuteAsync(
                new CreateProductCommand(
                    $"نوشابه گازدار {index}",
                    $"SODA-{index}",
                    category.Value,
                    defaults.EachUnitId,
                    Money.FromTomans(20_000),
                    [$"626000000300{index}"]),
                CancellationToken.None);
        }

        var reader = new FirebirdProductSearchReader(factory);
        var handler = new SearchProductsHandler(reader);

        var results = await handler.ExecuteAsync(
            new SearchProductsQuery("نوشابه", MaxResults: 3),
            CancellationToken.None);

        Assert.Equal(3, results.Count);
    }

    private sealed record TestUserContext(Guid UserId) : IUserContext;

    private sealed record TestClock(DateTimeOffset UtcNow) : IClock;
}
