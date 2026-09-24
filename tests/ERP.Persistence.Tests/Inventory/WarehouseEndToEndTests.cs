using ERP.Application.Catalog;
using ERP.Application.Common;
using ERP.Application.Inventory;
using ERP.Domain.Common;
using ERP.Persistence.Database;
using ERP.Persistence.Services;
using ERP.Persistence.Tests.TestSupport;

namespace ERP.Persistence.Tests.Inventory;

/// <summary>
/// «لیست انبارها» against a real Firebird: the list, the active-only name rule (a Firebird 5
/// partial unique index), and every reason «حذف» is refused.
/// </summary>
public sealed class WarehouseEndToEndTests
{
    [Fact]
    public async Task CreateEditSearchAndArchiveAWarehouse()
    {
        await using var database = FirebirdTestDatabase.Create();
        var (setup, defaults) = await SetupAsync(database);

        var created = await setup.ExecuteAsync(new CreateWarehouseCommand("انبار شمال", "تهران"), CancellationToken.None);
        Assert.True(created.IsSuccess);
        var edited = await setup.ExecuteAsync(
            new UpdateWarehouseCommand(created.Value, "انبار شمال شهر", null), CancellationToken.None);
        Assert.True(edited.IsSuccess);

        var all = await setup.ExecuteAsync(new WarehouseListQuery(null), CancellationToken.None);
        Assert.Equal(["انبار شمال شهر", "فروشگاه مرکزی"], all.Select(row => row.Name).ToArray());
        Assert.Null(all[0].Address);

        var search = await setup.ExecuteAsync(new WarehouseListQuery("شمال"), CancellationToken.None);
        Assert.Equal(created.Value, Assert.Single(search).Id);

        var archived = await setup.ExecuteAsync(new ArchiveWarehouseCommand(created.Value), CancellationToken.None);
        Assert.True(archived.IsSuccess);
        var afterArchive = await setup.ExecuteAsync(new WarehouseListQuery(null), CancellationToken.None);
        Assert.Equal(defaults.MainWarehouseId, Assert.Single(afterArchive).Id);
    }

    [Fact]
    public async Task ANameIsUniqueAmongActiveWarehousesOnlySoAnArchivedNameCanBeReused()
    {
        await using var database = FirebirdTestDatabase.Create();
        var (setup, _) = await SetupAsync(database);

        var first = await setup.ExecuteAsync(new CreateWarehouseCommand("انبار شمال", null), CancellationToken.None);
        var duplicate = await setup.ExecuteAsync(new CreateWarehouseCommand("انبار شمال", null), CancellationToken.None);
        Assert.Equal("inventory.warehouse.duplicate-name", duplicate.Error?.Code);

        await setup.ExecuteAsync(new ArchiveWarehouseCommand(first.Value), CancellationToken.None);
        var reused = await setup.ExecuteAsync(new CreateWarehouseCommand("انبار شمال", null), CancellationToken.None);

        Assert.True(reused.IsSuccess);
        Assert.NotEqual(first.Value, reused.Value);
    }

    [Fact]
    public async Task TheMainWarehouseAndAWarehouseWithStockAreNotArchived()
    {
        await using var database = FirebirdTestDatabase.Create();
        var (setup, defaults) = await SetupAsync(database);
        var second = await setup.ExecuteAsync(new CreateWarehouseCommand("انبار دوم", null), CancellationToken.None);
        var category = await setup.ExecuteAsync(new CreateCategoryCommand("مواد غذایی", null, 1), CancellationToken.None);
        var rice = await setup.ExecuteAsync(
            new CreateProductCommand("برنج", "RICE-1", category.Value, defaults.EachUnitId, Money.FromTomans(245_000), []),
            CancellationToken.None);
        await setup.ExecuteAsync(
            new ReceiveOpeningStockCommand(rice.Value, second.Value, 5, 200_000_0, new DateOnly(2026, 9, 1)),
            CancellationToken.None);

        var main = await setup.ExecuteAsync(new ArchiveWarehouseCommand(defaults.MainWarehouseId), CancellationToken.None);
        var stocked = await setup.ExecuteAsync(new ArchiveWarehouseCommand(second.Value), CancellationToken.None);

        Assert.Equal("inventory.warehouse.main", main.Error?.Code);
        Assert.Equal("inventory.warehouse.has-stock", stocked.Error?.Code);
        Assert.Equal(2, (await setup.ExecuteAsync(new WarehouseListQuery(null), CancellationToken.None)).Count);
    }

    private static async Task<(FirebirdRetailSetupService Setup, RetailSetupDefaults Defaults)> SetupAsync(
        FirebirdTestDatabase database)
    {
        var factory = new FirebirdConnectionFactory(database.Options);
        var defaults = await new FirebirdDatabaseBootstrapper(factory).InitializeAsync(CancellationToken.None);
        var clock = new TestClock { UtcNow = new DateTimeOffset(2026, 9, 24, 8, 0, 0, TimeSpan.Zero) };
        return (new FirebirdRetailSetupService(factory, new TestUserContext(Guid.NewGuid()), clock, AllowAllAccess.Instance), defaults);
    }

    private sealed record TestUserContext(Guid UserId) : IUserContext;

    private sealed class TestClock : IClock
    {
        public required DateTimeOffset UtcNow { get; set; }
    }
}
