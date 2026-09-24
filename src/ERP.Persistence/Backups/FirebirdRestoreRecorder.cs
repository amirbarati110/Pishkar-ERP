using System.Globalization;
using ERP.Application.Audit;
using ERP.Application.Backups;
using ERP.Domain.Backups;
using ERP.Persistence.Audit;
using ERP.Persistence.Database;
using ERP.Persistence.Migrations;
using ERP.Persistence.Services;

namespace ERP.Persistence.Backups;

/// <summary>
/// Runs right after a restore, against the database that is now live. A backup can come from an
/// older version of the app, so its schema is migrated first; then the restore is written into
/// that database's own history — the audit row, and the safety copy as a backup row so «the way
/// back» shows on the backup page.
/// </summary>
public sealed class FirebirdRestoreRecorder : IRestoreRecorder
{
    private readonly FirebirdConnectionFactory _connectionFactory;

    public FirebirdRestoreRecorder(FirebirdConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task RecordAsync(RestoreRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);

        await new MigrationRunner(_connectionFactory, FirebirdDatabaseBootstrapper.Migrations)
            .MigrateAsync(cancellationToken)
            .ConfigureAwait(false);

        await using var unitOfWork = await FirebirdUnitOfWork
            .CreateAsync(_connectionFactory, cancellationToken)
            .ConfigureAwait(false);
        var safetyCopy = BackupRecord.Create(record.SafetyBackupPath, record.SafetyBackupSizeBytes, record.RestoredAtUtc);
        await new FirebirdBackupRecordRepository(unitOfWork).SaveAsync(safetyCopy, cancellationToken).ConfigureAwait(false);
        await new FirebirdAuditWriter(unitOfWork).WriteAsync(
            new AuditEntry(
                Guid.NewGuid(),
                record.ActorUserId,
                "backups.backup.restored",
                nameof(BackupRecord),
                safetyCopy.Id.ToString(),
                null,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"from={record.RestoredFromPath};safety={record.SafetyBackupPath}"),
                record.RestoredAtUtc),
            cancellationToken).ConfigureAwait(false);
        await unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}
