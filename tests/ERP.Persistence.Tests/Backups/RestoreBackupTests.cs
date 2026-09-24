using System.Globalization;
using ERP.Application.Backups;
using ERP.Application.Catalog;
using ERP.Application.Common;
using ERP.Application.Identity;
using ERP.Domain.Common;
using ERP.Persistence.Backups;
using ERP.Persistence.Database;
using ERP.Persistence.Identity;
using ERP.Persistence.Services;
using ERP.Persistence.Tests.TestSupport;
using FirebirdSql.Data.FirebirdClient;

namespace ERP.Persistence.Tests.Backups;

/// <summary>
/// «بازیابی نسخه پشتیبان» on a real embedded Firebird: the live database genuinely replaced, the
/// safety copy genuinely usable to go back, and every refusal leaving the live data untouched.
/// </summary>
public sealed class RestoreBackupTests
{
    private const string AdminUser = "admin";
    private const string AdminPassword = "Pishkar-1405";

    [Fact]
    public async Task RestoringBringsBackTheBackedUpDataAndTheSafetyCopyBringsTodaysBack()
    {
        await using var database = FirebirdTestDatabase.Create();
        var c = await SetupAsync(database);
        try
        {
            await AddProductAsync(c, "برنج قبل از پشتیبان", "BEFORE");
            var backup = await c.Backups.ExecuteAsync(new CreateBackupCommand(c.Directory), CancellationToken.None);
            Assert.True(backup.IsSuccess, backup.Error?.Message);
            var backupPath = await BackupPathAsync(c, backup.Value);
            await AddProductAsync(c, "روغن بعد از پشتیبان", "AFTER");
            Assert.Equal(["AFTER", "BEFORE"], await SkusAsync(c));

            var restored = await c.Backups.ExecuteAsync(
                new RestoreBackupCommand(backupPath, c.Directory, AdminUser, AdminPassword), CancellationToken.None);

            Assert.True(restored.IsSuccess, restored.Error?.Message);
            Assert.Null(restored.Value!.Warning);
            Assert.Equal(["BEFORE"], await SkusAsync(c));          // the later product is gone
            Assert.True(File.Exists(restored.Value.SafetyBackupPath));
            Assert.Equal(1, await CountAsync(c, "SELECT COUNT(*) FROM AUDIT_ENTRY WHERE ACTION_NAME = 'backups.backup.restored'"));
            var health = await c.Backups.ExecuteAsync(CancellationToken.None);
            Assert.Equal(restored.Value.SafetyBackupPath, health.LatestBackupFilePath); // the way back is on the page
            Assert.True(health.MigrationsUpToDate);

            // the wrong file was chosen after all: the safety copy brings today's data back
            var undone = await c.Backups.ExecuteAsync(
                new RestoreBackupCommand(restored.Value.SafetyBackupPath, c.Directory, AdminUser, AdminPassword), CancellationToken.None);
            Assert.True(undone.IsSuccess, undone.Error?.Message);
            Assert.Equal(["AFTER", "BEFORE"], await SkusAsync(c));
        }
        finally
        {
            DeleteDirectory(c.Directory);
        }
    }

    [Fact]
    public async Task WithoutTheManagersPasswordNothingIsTouched()
    {
        await using var database = FirebirdTestDatabase.Create();
        var c = await SetupAsync(database);
        try
        {
            var backup = await c.Backups.ExecuteAsync(new CreateBackupCommand(c.Directory), CancellationToken.None);
            var backupPath = await BackupPathAsync(c, backup.Value);
            await AddProductAsync(c, "روغن", "AFTER");

            var refused = await c.Backups.ExecuteAsync(
                new RestoreBackupCommand(backupPath, c.Directory, AdminUser, "wrong-password"), CancellationToken.None);

            Assert.Equal("backup.restore.not-admin", refused.Error?.Code);
            Assert.Equal(["AFTER"], await SkusAsync(c));
            Assert.Single(Directory.GetFiles(c.Directory)); // no safety copy was even taken
        }
        finally
        {
            DeleteDirectory(c.Directory);
        }
    }

    [Fact]
    public async Task ABrokenFileIsRefusedBeforeTheLiveDataIsTouched()
    {
        await using var database = FirebirdTestDatabase.Create();
        var c = await SetupAsync(database);
        try
        {
            await AddProductAsync(c, "روغن", "KEEP");
            Directory.CreateDirectory(c.Directory);
            var broken = Path.Combine(c.Directory, "broken.fbk");
            await File.WriteAllBytesAsync(broken, Enumerable.Range(0, 4096).Select(index => (byte)(index * 7)).ToArray());

            var refused = await c.Backups.ExecuteAsync(
                new RestoreBackupCommand(broken, c.Directory, AdminUser, AdminPassword), CancellationToken.None);

            Assert.Equal("backup.restore.unusable", refused.Error?.Code);
            Assert.Equal(["KEEP"], await SkusAsync(c));
            Assert.DoesNotContain(Directory.GetFiles(c.Directory), path => Path.GetFileName(path).StartsWith("before-restore", StringComparison.Ordinal));
        }
        finally
        {
            DeleteDirectory(c.Directory);
        }
    }

    [Fact]
    public async Task AMissingFileIsRefused()
    {
        await using var database = FirebirdTestDatabase.Create();
        var c = await SetupAsync(database);

        var refused = await c.Backups.ExecuteAsync(
            new RestoreBackupCommand(Path.Combine(c.Directory, "nope.fbk"), c.Directory, AdminUser, AdminPassword), CancellationToken.None);

        Assert.Equal("backup.restore.file-missing", refused.Error?.Code);
    }

    private static async Task<Context> SetupAsync(FirebirdTestDatabase database)
    {
        var factory = new FirebirdConnectionFactory(database.Options);
        var defaults = await new FirebirdDatabaseBootstrapper(factory).InitializeAsync(CancellationToken.None);
        var admin = await new FirebirdIdentityService(factory).ExecuteAsync(
            new RegisterFirstAdminCommand(AdminUser, "مدیر", AdminPassword), CancellationToken.None);
        Assert.True(admin.IsSuccess, admin.Error?.Message);
        var user = new TestUserContext(admin.Value.Value);
        var clock = new TestClock();
        var setup = new FirebirdRetailSetupService(factory, user, clock);
        var category = await setup.ExecuteAsync(new CreateCategoryCommand("مواد غذایی", null, 1), CancellationToken.None);
        return new Context(
            factory,
            defaults,
            setup,
            category.Value,
            new FirebirdBackupService(factory, database.Options, user, clock),
            Path.Combine(Path.GetTempPath(), $"pishkar-restore-test-{Guid.NewGuid():N}"));
    }

    private static async Task AddProductAsync(Context c, string name, string sku)
    {
        var created = await c.Setup.ExecuteAsync(
            new CreateProductCommand(name, sku, c.CategoryId, c.Defaults.EachUnitId, Money.FromTomans(100_000), []),
            CancellationToken.None);
        Assert.True(created.IsSuccess, created.Error?.Message);
    }

    private static async Task<string[]> SkusAsync(Context c)
    {
        await using var connection = await c.Factory.OpenAsync(CancellationToken.None);
        await using var command = new FbCommand("SELECT SKU FROM PRODUCT ORDER BY SKU", connection);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
        var skus = new List<string>();
        while (await reader.ReadAsync(CancellationToken.None))
        {
            skus.Add(reader.GetString(0));
        }

        return [.. skus];
    }

    private static async Task<int> CountAsync(Context c, string sql)
    {
        await using var connection = await c.Factory.OpenAsync(CancellationToken.None);
        await using var command = new FbCommand(sql, connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync(CancellationToken.None), CultureInfo.InvariantCulture);
    }

    private static async Task<string> BackupPathAsync(Context c, ERP.Domain.Backups.BackupRecordId id)
    {
        await using var unitOfWork = await FirebirdUnitOfWork.CreateAsync(c.Factory, CancellationToken.None);
        return (await new FirebirdBackupRecordRepository(unitOfWork).GetByIdAsync(id, CancellationToken.None))!.FilePath;
    }

    private static void DeleteDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed record Context(
        FirebirdConnectionFactory Factory,
        RetailSetupDefaults Defaults,
        FirebirdRetailSetupService Setup,
        ERP.Domain.Catalog.CategoryId CategoryId,
        FirebirdBackupService Backups,
        string Directory);

    private sealed record TestUserContext(Guid UserId) : IUserContext;

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }
}
