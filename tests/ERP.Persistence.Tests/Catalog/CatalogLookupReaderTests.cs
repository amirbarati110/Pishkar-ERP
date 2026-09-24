using ERP.Application.Catalog;
using ERP.Application.Common;
using ERP.Domain.Common;
using ERP.Persistence.Catalog;
using ERP.Persistence.Database;
using ERP.Persistence.Services;
using ERP.Persistence.Tests.TestSupport;

namespace ERP.Persistence.Tests.Catalog;

public sealed class CatalogLookupReaderTests
{
    [Fact]
    public async Task LoadReturnsPersistedCategoriesUnitsAndProducts()
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
        var product = await setup.ExecuteAsync(
            new CreateProductCommand(
                "آب معدنی",
                "WATER-1",
                category.Value,
                defaults.EachUnitId,
                Money.FromTomans(12_000),
                ["6260000001002"]),
            CancellationToken.None);
        Assert.True(category.IsSuccess);
        Assert.True(product.IsSuccess);

        var reader = new FirebirdCatalogLookupReader(factory);
        var snapshot = await reader.LoadAsync(CancellationToken.None);

        Assert.Contains(snapshot.Categories, item => item.Name == "نوشیدنی");
        Assert.Contains(snapshot.Units, item => item.Name == "عدد");
        Assert.Contains(snapshot.Products, item => item.Name == "آب معدنی");
    }

    private sealed record TestUserContext(Guid UserId) : IUserContext;

    private sealed record TestClock(DateTimeOffset UtcNow) : IClock;
}
