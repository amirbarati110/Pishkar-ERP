using ERP.Application.Audit;
using ERP.Application.Common;
using ERP.Domain.Backups;

namespace ERP.Application.Backups;

public sealed record VerifyBackupCommand(BackupRecordId BackupRecordId);

public sealed record VerifyBackupResult(bool IsVerified, string Note);

public interface IVerifyBackupHandler
{
    Task<Result<VerifyBackupResult>> ExecuteAsync(VerifyBackupCommand command, CancellationToken cancellationToken);
}

/// <summary>
/// «بازیابی آزمایشی» (§15 acceptance criterion 11) — genuinely restores the
/// backup file into a throwaway database and checks it structurally (§16:
/// a backup that was never read back is not «سالم», just a file that exists).
/// </summary>
public sealed class VerifyBackupHandler : IVerifyBackupHandler
{
    private readonly IBackupRecordRepository _records;
    private readonly IBackupEngine _engine;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;

    public VerifyBackupHandler(
        IBackupRecordRepository records,
        IBackupEngine engine,
        IAuditWriter audit,
        IUnitOfWork unitOfWork,
        IUserContext userContext,
        IClock clock)
    {
        _records = records;
        _engine = engine;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _userContext = userContext;
        _clock = clock;
    }

    public async Task<Result<VerifyBackupResult>> ExecuteAsync(VerifyBackupCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var record = await _records.GetByIdAsync(command.BackupRecordId, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return Result.Failure<VerifyBackupResult>("backup.verify.not-found", "نسخه پشتیبان پیدا نشد.");
        }

        StructuralCheckResult check;
        try
        {
            check = await _engine.RestoreAndCheckAsync(record.FilePath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            check = new StructuralCheckResult(false, exception.Message);
        }

        var now = _clock.UtcNow;
        if (check.Success)
        {
            record.MarkVerified(now, check.Note);
        }
        else
        {
            record.MarkVerificationFailed(now, check.Note);
        }

        await _records.SaveAsync(record, cancellationToken).ConfigureAwait(false);
        await _audit.WriteAsync(
            new AuditEntry(
                Guid.NewGuid(),
                _userContext.UserId,
                check.Success ? "backups.backup.verified" : "backups.backup.verification-failed",
                nameof(BackupRecord),
                record.Id.ToString(),
                null,
                check.Note,
                now),
            cancellationToken).ConfigureAwait(false);
        await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(new VerifyBackupResult(check.Success, check.Note));
    }
}
