using ERP.Application.Backups;
using ERP.Application.Tests.TestDoubles;
using ERP.Domain.Backups;

namespace ERP.Application.Tests.Backups;

public sealed class CreateBackupTests
{
    [Fact]
    public async Task ASuccessfulBackupIsRecordedNotVerifiedYet()
    {
        var context = new ApplicationTestContext();
        var engine = new FakeBackupEngine { BackupSizeBytes = 50_000 };

        var result = await Handler(context, engine).ExecuteAsync(
            new CreateBackupCommand(@"D:\backups"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var record = Assert.Single(context.BackupRecords.Items);
        Assert.Equal(BackupVerificationStatus.NotVerified, record.Status);
        Assert.Equal(50_000, record.SizeBytes);
        Assert.Equal(1, context.UnitOfWork.CommitCount);
        Assert.Contains(context.Audit.Entries, entry => entry.Action == "backups.backup.created");
    }

    [Fact]
    public async Task AFailedBackupIsNotRecordedAtAll()
    {
        var context = new ApplicationTestContext();
        var engine = new FakeBackupEngine { ThrowOnBackup = true };

        var result = await Handler(context, engine).ExecuteAsync(
            new CreateBackupCommand(@"D:\backups"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("backup.create.failed", result.Error?.Code);
        Assert.Empty(context.BackupRecords.Items);
        Assert.Equal(0, context.UnitOfWork.CommitCount);
    }

    private static CreateBackupHandler Handler(ApplicationTestContext context, FakeBackupEngine engine) => new(
        engine, context.BackupRecords, context.Audit, context.UnitOfWork, context.User, context.Clock);
}

public sealed class VerifyBackupTests
{
    [Fact]
    public async Task ARestorableBackupIsMarkedVerified()
    {
        var context = new ApplicationTestContext();
        var record = BackupRecord.Create(@"D:\backups\backup-1.fbk", 50_000, context.Clock.UtcNow);
        context.BackupRecords.Items.Add(record);
        var engine = new FakeBackupEngine { CheckResult = new StructuralCheckResult(true, "۴ جدول اصلی بازیابی و شمارش شدند.") };

        var result = await Handler(context, engine).ExecuteAsync(
            new VerifyBackupCommand(record.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsVerified);
        Assert.Equal(BackupVerificationStatus.Verified, record.Status);
        Assert.Contains(context.Audit.Entries, entry => entry.Action == "backups.backup.verified");
    }

    [Fact]
    public async Task ABrokenBackupIsMarkedFailedRatherThanThrowing()
    {
        var context = new ApplicationTestContext();
        var record = BackupRecord.Create(@"D:\backups\backup-1.fbk", 50_000, context.Clock.UtcNow);
        context.BackupRecords.Items.Add(record);
        var engine = new FakeBackupEngine { ThrowOnRestore = true };

        var result = await Handler(context, engine).ExecuteAsync(
            new VerifyBackupCommand(record.Id), CancellationToken.None);

        Assert.True(result.IsSuccess); // اجرای handler خودش شکست نمی‌خورد؛ نتیجه‌ی بررسی شکست است
        Assert.False(result.Value!.IsVerified);
        Assert.Equal(BackupVerificationStatus.Failed, record.Status);
        Assert.Contains(context.Audit.Entries, entry => entry.Action == "backups.backup.verification-failed");
    }

    [Fact]
    public async Task VerifyingAMissingBackupFails()
    {
        var context = new ApplicationTestContext();
        var engine = new FakeBackupEngine();

        var result = await Handler(context, engine).ExecuteAsync(
            new VerifyBackupCommand(BackupRecordId.New()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("backup.verify.not-found", result.Error?.Code);
    }

    private static VerifyBackupHandler Handler(ApplicationTestContext context, FakeBackupEngine engine) => new(
        context.BackupRecords, engine, context.Audit, context.UnitOfWork, context.User, context.Clock);
}

public sealed class GetSystemHealthTests
{
    [Fact]
    public async Task ReportsUpToDateWhenAppliedCountMeetsExpected()
    {
        var context = new ApplicationTestContext();
        context.BackupRecords.Items.Add(BackupRecord.Create(@"D:\backups\backup-1.fbk", 1000, context.Clock.UtcNow));
        var migrations = new FakeMigrationStatusReader { Status = new MigrationStatus(11, 11) };

        var health = await new GetSystemHealthHandler(migrations, context.BackupRecords, expectedMigrationCount: 11)
            .ExecuteAsync(CancellationToken.None);

        Assert.True(health.DatabaseReachable);
        Assert.True(health.MigrationsUpToDate);
        Assert.NotNull(health.LatestBackupId);
    }

    [Fact]
    public async Task ReportsNotUpToDateWhenFewerMigrationsAreAppliedThanExpected()
    {
        var context = new ApplicationTestContext();
        var migrations = new FakeMigrationStatusReader { Status = new MigrationStatus(9, 9) };

        var health = await new GetSystemHealthHandler(migrations, context.BackupRecords, expectedMigrationCount: 11)
            .ExecuteAsync(CancellationToken.None);

        Assert.False(health.MigrationsUpToDate);
        Assert.Equal(9, health.AppliedMigrationCount);
    }

    [Fact]
    public async Task AnUnreachableDatabaseIsReportedRatherThanThrowing()
    {
        var context = new ApplicationTestContext();
        var migrations = new FakeMigrationStatusReader { Throw = true };

        var health = await new GetSystemHealthHandler(migrations, context.BackupRecords, expectedMigrationCount: 11)
            .ExecuteAsync(CancellationToken.None);

        Assert.False(health.DatabaseReachable);
        Assert.NotNull(health.DatabaseError);
    }

    private sealed class FakeMigrationStatusReader : IMigrationStatusReader
    {
        public MigrationStatus Status { get; set; } = new(0, null);

        public bool Throw { get; set; }

        public Task<MigrationStatus> ReadAsync(CancellationToken cancellationToken) =>
            Throw ? throw new InvalidOperationException("database is away") : Task.FromResult(Status);
    }
}

internal sealed class FakeBackupEngine : IBackupEngine
{
    public long BackupSizeBytes { get; set; } = 1000;

    public bool ThrowOnBackup { get; set; }

    public bool ThrowOnRestore { get; set; }

    public StructuralCheckResult CheckResult { get; set; } = new(true, "بررسی موفق بود.");

    public Task<long> CreateBackupFileAsync(string destinationFilePath, CancellationToken cancellationToken) =>
        ThrowOnBackup ? throw new InvalidOperationException("backup engine is away") : Task.FromResult(BackupSizeBytes);

    public Task<StructuralCheckResult> RestoreAndCheckAsync(string backupFilePath, CancellationToken cancellationToken) =>
        ThrowOnRestore ? throw new InvalidOperationException("restore engine is away") : Task.FromResult(CheckResult);
}
