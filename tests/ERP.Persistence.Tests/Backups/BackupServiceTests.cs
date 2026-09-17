using ERP.Application.Backups;
using ERP.Application.Common;
using ERP.Domain.Backups;
using ERP.Persistence.Backups;
using ERP.Persistence.Database;
using ERP.Persistence.Services;
using ERP.Persistence.Tests.TestSupport;

namespace ERP.Persistence.Tests.Backups;

/// <summary>
/// «Backup محلی قابل اعتبارسنجی» on real embedded Firebird — the whole point
/// of this feature is that a backup is only trusted once it has genuinely
/// been restored and read back (§16), so these tests exercise the real
/// gbak-level round trip, not a stand-in.
/// </summary>
public sealed class BackupServiceTests
{
    [Fact]
    public async Task ABackupOfARealDatabaseCanBeVerifiedByGenuinelyRestoringIt()
    {
        var database = FirebirdTestDatabase.Create();
        await using var _ = database;
        var factory = new FirebirdConnectionFactory(database.Options);
        await new FirebirdDatabaseBootstrapper(factory).InitializeAsync(CancellationToken.None);

        var service = new FirebirdBackupService(factory, database.Options, new TestUserContext(Guid.NewGuid()), new TestClock());
        var backupDirectory = Path.Combine(Path.GetTempPath(), $"pishkar-backup-test-{Guid.NewGuid():N}");

        try
        {
            var created = await service.ExecuteAsync(new CreateBackupCommand(backupDirectory), CancellationToken.None);
            Assert.True(created.IsSuccess, created.Error?.Message);

            await using (var unitOfWork = await FirebirdUnitOfWork.CreateAsync(factory, CancellationToken.None))
            {
                var record = await new FirebirdBackupRecordRepository(unitOfWork).GetByIdAsync(created.Value, CancellationToken.None);
                Assert.NotNull(record);
                Assert.True(record!.SizeBytes > 0);
                Assert.True(File.Exists(record.FilePath));
                Assert.Equal(BackupVerificationStatus.NotVerified, record.Status);
            }

            var verified = await service.ExecuteAsync(new VerifyBackupCommand(created.Value), CancellationToken.None);

            Assert.True(verified.IsSuccess);
            Assert.True(verified.Value!.IsVerified);
            Assert.Contains("بازیابی موفق بود", verified.Value.Note, StringComparison.Ordinal);

            await using var checkUnitOfWork = await FirebirdUnitOfWork.CreateAsync(factory, CancellationToken.None);
            var reloaded = await new FirebirdBackupRecordRepository(checkUnitOfWork).GetByIdAsync(created.Value, CancellationToken.None);
            Assert.Equal(BackupVerificationStatus.Verified, reloaded!.Status);
            Assert.NotNull(reloaded.VerifiedAtUtc);
        }
        finally
        {
            if (Directory.Exists(backupDirectory))
            {
                Directory.Delete(backupDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task HealthReportsMigrationsUpToDateAndTheLatestBackupOnceOneExists()
    {
        var database = FirebirdTestDatabase.Create();
        await using var _ = database;
        var factory = new FirebirdConnectionFactory(database.Options);
        await new FirebirdDatabaseBootstrapper(factory).InitializeAsync(CancellationToken.None);
        var service = new FirebirdBackupService(factory, database.Options, new TestUserContext(Guid.NewGuid()), new TestClock());

        var healthBefore = await service.ExecuteAsync(CancellationToken.None);
        Assert.True(healthBefore.DatabaseReachable);
        Assert.True(healthBefore.MigrationsUpToDate);
        Assert.Null(healthBefore.LatestBackupId);

        var backupDirectory = Path.Combine(Path.GetTempPath(), $"pishkar-backup-test-{Guid.NewGuid():N}");
        try
        {
            var created = await service.ExecuteAsync(new CreateBackupCommand(backupDirectory), CancellationToken.None);
            Assert.True(created.IsSuccess);

            var healthAfter = await service.ExecuteAsync(CancellationToken.None);

            Assert.Equal(created.Value, healthAfter.LatestBackupId);
            Assert.Equal(BackupVerificationStatus.NotVerified, healthAfter.LatestBackupStatus);
        }
        finally
        {
            if (Directory.Exists(backupDirectory))
            {
                Directory.Delete(backupDirectory, recursive: true);
            }
        }
    }

    private sealed record TestUserContext(Guid UserId) : IUserContext;

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }
}
