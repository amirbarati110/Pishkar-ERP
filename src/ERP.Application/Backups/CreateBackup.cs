using ERP.Application.Audit;
using ERP.Application.Common;
using ERP.Domain.Backups;
using ERP.Domain.Common;

namespace ERP.Application.Backups;

public sealed record CreateBackupCommand(string DestinationDirectory);

public interface ICreateBackupHandler
{
    Task<Result<BackupRecordId>> ExecuteAsync(CreateBackupCommand command, CancellationToken cancellationToken);
}

/// <summary>«Backup محلی» (milestone-1 §15.10/§16) — takes the backup first, only records it once the file genuinely exists; a failed backup never becomes a row someone might trust later.</summary>
public sealed class CreateBackupHandler : ICreateBackupHandler
{
    private readonly IBackupEngine _engine;
    private readonly IBackupRecordRepository _records;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;

    public CreateBackupHandler(
        IBackupEngine engine,
        IBackupRecordRepository records,
        IAuditWriter audit,
        IUnitOfWork unitOfWork,
        IUserContext userContext,
        IClock clock)
    {
        _engine = engine;
        _records = records;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _userContext = userContext;
        _clock = clock;
    }

    public async Task<Result<BackupRecordId>> ExecuteAsync(CreateBackupCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var now = _clock.UtcNow;
        var fileName = $"backup-{now:yyyyMMdd-HHmmss}.fbk";
        var destinationPath = Path.Combine(command.DestinationDirectory, fileName);

        long sizeBytes;
        try
        {
            Directory.CreateDirectory(command.DestinationDirectory);
            sizeBytes = await _engine.CreateBackupFileAsync(destinationPath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Result.Failure<BackupRecordId>(
                "backup.create.failed", $"گرفتن نسخه پشتیبان ناموفق بود: {exception.Message}");
        }

        try
        {
            var record = BackupRecord.Create(destinationPath, sizeBytes, now);
            await _records.SaveAsync(record, cancellationToken).ConfigureAwait(false);
            await _audit.WriteAsync(
                new AuditEntry(
                    Guid.NewGuid(),
                    _userContext.UserId,
                    "backups.backup.created",
                    nameof(BackupRecord),
                    record.Id.ToString(),
                    null,
                    $"path={destinationPath};size={sizeBytes}",
                    now),
                cancellationToken).ConfigureAwait(false);
            await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success(record.Id);
        }
        catch (DomainException exception)
        {
            return Result.Failure<BackupRecordId>("backup.create.invalid", exception.Message);
        }
    }
}
