using System.Globalization;
using ERP.Persistence.Database;
using ERP.Persistence.Services;
using ERP.Persistence.Tests.TestSupport;
using FirebirdSql.Data.FirebirdClient;

namespace ERP.Persistence.Tests.Services;

public sealed class FirebirdDatabaseBootstrapperTests
{
    [Fact]
    public async Task InitializeTwiceMigratesAndSeedsDefaultsOnlyOnce()
    {
        await using var database = FirebirdTestDatabase.Create();
        var factory = new FirebirdConnectionFactory(database.Options);
        var bootstrapper = new FirebirdDatabaseBootstrapper(factory);

        var first = await bootstrapper.InitializeAsync(CancellationToken.None);
        var second = await bootstrapper.InitializeAsync(CancellationToken.None);

        Assert.Equal(first, second);
        Assert.Equal(1, await CountAsync(factory, "CATEGORY"));
        Assert.Equal(1, await CountAsync(factory, "PRODUCT_UNIT"));
    }

    private static async Task<int> CountAsync(
        FirebirdConnectionFactory factory,
        string tableName)
    {
        await using var connection = await factory.OpenAsync(CancellationToken.None);
        await using var command = new FbCommand($"SELECT COUNT(*) FROM {tableName}", connection);
        var result = await command.ExecuteScalarAsync(CancellationToken.None);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }
}
