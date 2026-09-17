using ERP.Domain.Backups;

namespace ERP.Application.Backups;

public interface IBackupRecordRepository
{
    Task SaveAsync(BackupRecord record, CancellationToken cancellationToken);

    Task<BackupRecord?> GetByIdAsync(BackupRecordId id, CancellationToken cancellationToken);

    Task<BackupRecord?> GetLatestAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<BackupRecord>> ListRecentAsync(int count, CancellationToken cancellationToken);
}
