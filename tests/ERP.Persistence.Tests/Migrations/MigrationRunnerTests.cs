using FirebirdSql.Data.FirebirdClient;
using ERP.Persistence.Database;
using ERP.Persistence.Migrations;
using ERP.Persistence.Services;
using ERP.Persistence.Tests.TestSupport;

namespace ERP.Persistence.Tests.Migrations;

public sealed class MigrationRunnerTests
{
    /// <summary>
    /// «Migration Test از دیتابیس خالی و نسخه قبلی» (§13): simulates upgrading
    /// an existing installation whose database only has an older subset of
    /// migrations applied — a fresh <see cref="MigrationRunner"/> built from
    /// the *current* full list must apply exactly the missing ones (and not
    /// error trying to re-apply, or re-create tables for, the ones already
    /// there) and leave the database on the latest version.
    /// </summary>
    [Fact]
    public async Task MigratingAnOlderPartiallyMigratedDatabaseAppliesOnlyWhatIsMissing()
    {
        await using var database = FirebirdTestDatabase.Create();
        var connectionFactory = new FirebirdConnectionFactory(database.Options);
        var allMigrations = FirebirdDatabaseBootstrapper.Migrations;
        var olderSubset = allMigrations.Take(allMigrations.Count / 2).ToArray();

        // "نسخه قبلی برنامه" — فقط نیمه‌ی اول migration ها را روی این دیتابیس اجرا کرده بود.
        await new MigrationRunner(connectionFactory, olderSubset).MigrateAsync(CancellationToken.None);

        // "نسخه فعلی برنامه" — همان دیتابیس را با کد امروز باز می‌کند.
        await new MigrationRunner(connectionFactory, allMigrations).MigrateAsync(CancellationToken.None);

        await using var connection = await connectionFactory.OpenAsync(CancellationToken.None);
        await using var countCommand = new FbCommand("SELECT COUNT(*) FROM SCHEMA_MIGRATIONS", connection);
        var appliedCount = Convert.ToInt32(
            await countCommand.ExecuteScalarAsync(CancellationToken.None),
            System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(allMigrations.Count, appliedCount);

        // جدولی که فقط در نیمه‌ی دوم ساخته می‌شود (مثلاً APP_USER) باید حالا موجود باشد.
        await using var tableCommand = new FbCommand(
            "SELECT COUNT(*) FROM RDB$RELATIONS WHERE RDB$RELATION_NAME = 'APP_USER'", connection);
        var tableExists = Convert.ToInt32(
            await tableCommand.ExecuteScalarAsync(CancellationToken.None),
            System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(1, tableExists);
    }

    [Fact]
    public async Task MigrateAsyncAppliesFirstMigrationOnlyOnce()
    {
        await using var database = FirebirdTestDatabase.Create();
        var connectionFactory = new FirebirdConnectionFactory(database.Options);
        var runner = new MigrationRunner(connectionFactory, [new V001CreateCatalogAndInventory()]);

        await runner.MigrateAsync(CancellationToken.None);
        await runner.MigrateAsync(CancellationToken.None);

        await using var connection = await connectionFactory.OpenAsync(CancellationToken.None);
        await using var command = new FbCommand("SELECT COUNT(*) FROM SCHEMA_MIGRATIONS", connection);
        var count = Convert.ToInt32(
            await command.ExecuteScalarAsync(CancellationToken.None),
            System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(1, count);
    }

    [Theory]
    [InlineData("CATEGORY")]
    [InlineData("PRODUCT")]
    [InlineData("PRODUCT_BARCODE")]
    [InlineData("INVENTORY_LAYER")]
    [InlineData("STOCK_MOVEMENT")]
    [InlineData("AUDIT_ENTRY")]
    public async Task MigrateAsyncCreatesRequiredTable(string tableName)
    {
        await using var database = FirebirdTestDatabase.Create();
        var connectionFactory = new FirebirdConnectionFactory(database.Options);
        var runner = new MigrationRunner(connectionFactory, [new V001CreateCatalogAndInventory()]);
        await runner.MigrateAsync(CancellationToken.None);

        await using var connection = await connectionFactory.OpenAsync(CancellationToken.None);
        await using var command = new FbCommand(
            "SELECT COUNT(*) FROM RDB$RELATIONS WHERE RDB$RELATION_NAME = @name",
            connection);
        command.Parameters.AddWithValue("name", tableName);

        var count = Convert.ToInt32(
            await command.ExecuteScalarAsync(CancellationToken.None),
            System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(1, count);
    }
}
