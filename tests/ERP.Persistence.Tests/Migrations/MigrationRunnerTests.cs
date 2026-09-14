using FirebirdSql.Data.FirebirdClient;
using ERP.Persistence.Database;
using ERP.Persistence.Migrations;
using ERP.Persistence.Tests.TestSupport;

namespace ERP.Persistence.Tests.Migrations;

public sealed class MigrationRunnerTests
{
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
