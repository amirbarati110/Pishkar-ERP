using ERP.Domain.Backups;

namespace ERP.Application.Backups;

/// <summary>What was actually applied to the database, read from its own migration history table — not the code's own list, so a health check that runs against a real (possibly older) database tells the truth.</summary>
public sealed record MigrationStatus(int AppliedCount, int? LatestAppliedVersion);

public interface IMigrationStatusReader
{
    Task<MigrationStatus> ReadAsync(CancellationToken cancellationToken);
}

/// <summary>«سلامت سیستم را مشاهده کرد» (§15 acceptance criterion 11) — three plain facts, not a diagnostics dashboard: is the database reachable, are its migrations current, and how healthy is the most recent backup.</summary>
public sealed record SystemHealthView(
    bool DatabaseReachable,
    string? DatabaseError,
    int AppliedMigrationCount,
    int ExpectedMigrationCount,
    bool MigrationsUpToDate,
    BackupRecordId? LatestBackupId,
    DateTimeOffset? LatestBackupAtUtc,
    BackupVerificationStatus? LatestBackupStatus,
    string? LatestBackupFilePath = null);

public interface IGetSystemHealthHandler
{
    Task<SystemHealthView> ExecuteAsync(CancellationToken cancellationToken);
}

public sealed class GetSystemHealthHandler : IGetSystemHealthHandler
{
    private readonly IMigrationStatusReader _migrations;
    private readonly IBackupRecordRepository _backups;
    private readonly int _expectedMigrationCount;

    public GetSystemHealthHandler(IMigrationStatusReader migrations, IBackupRecordRepository backups, int expectedMigrationCount)
    {
        _migrations = migrations;
        _backups = backups;
        _expectedMigrationCount = expectedMigrationCount;
    }

    public async Task<SystemHealthView> ExecuteAsync(CancellationToken cancellationToken)
    {
        MigrationStatus status;
        try
        {
            status = await _migrations.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new SystemHealthView(false, exception.Message, 0, _expectedMigrationCount, false, null, null, null);
        }

        var latestBackup = await _backups.GetLatestAsync(cancellationToken).ConfigureAwait(false);

        return new SystemHealthView(
            true,
            null,
            status.AppliedCount,
            _expectedMigrationCount,
            status.AppliedCount >= _expectedMigrationCount,
            latestBackup?.Id,
            latestBackup?.CreatedAtUtc,
            latestBackup?.Status,
            latestBackup?.FilePath);
    }
}
