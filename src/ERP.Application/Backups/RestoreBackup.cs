using ERP.Application.Common;
using ERP.Application.Identity;

namespace ERP.Application.Backups;

/// <param name="BackupFilePath">The .fbk to bring back — one of the app's own backups, or a file brought from elsewhere (a new PC).</param>
/// <param name="SafetyBackupDirectory">Where the safety copy of today's data is written before anything is replaced.</param>
public sealed record RestoreBackupCommand(
    string BackupFilePath,
    string SafetyBackupDirectory,
    string AdminUsername,
    string AdminPassword);

/// <param name="SafetyBackupPath">The copy of the data as it was just before the restore — the way back if the wrong file was chosen.</param>
/// <param name="Warning">Set when the data was restored but writing the restore into its history failed; the restore itself stands.</param>
public sealed record RestoredBackup(string SafetyBackupPath, string? Warning);

/// <summary>What is written into the restored database once it is live — who restored what, and where the safety copy is.</summary>
public sealed record RestoreRecord(
    Guid ActorUserId,
    string RestoredFromPath,
    string SafetyBackupPath,
    long SafetyBackupSizeBytes,
    DateTimeOffset RestoredAtUtc);

/// <summary>Writes into the database that is live after a restore (the restored one), bringing its schema up to date first.</summary>
public interface IRestoreRecorder
{
    Task RecordAsync(RestoreRecord record, CancellationToken cancellationToken);
}

public interface IRestoreBackupHandler
{
    Task<Result<RestoredBackup>> ExecuteAsync(RestoreBackupCommand command, CancellationToken cancellationToken);
}

/// <summary>
/// «بازیابی نسخه پشتیبان» (§15.10–15.12). The one operation that replaces every record in the
/// store, so it is built to be undoable and never to leave a half-state:
/// <list type="number">
/// <item>only a manager's own credentials start it (§15.3 — the second of its two confirmations);</item>
/// <item>the file is first restored into a throwaway database and checked — a broken file never
/// touches the live data;</item>
/// <item>a safety backup of the live data is taken — if that fails, nothing is replaced;</item>
/// <item>the live database is replaced — if that fails, the safety backup is put back;</item>
/// <item>the restore is recorded in the restored database (audit + the safety copy as a backup
/// row, so the way back shows in the list).</item>
/// </list>
/// The app must be reopened afterwards: every screen still shows the old data.
/// </summary>
public sealed class RestoreBackupHandler : IRestoreBackupHandler
{
    private readonly IBackupEngine _engine;
    private readonly IVerifyAdminCredentialHandler _admin;
    private readonly IRestoreRecorder _recorder;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;

    public RestoreBackupHandler(
        IBackupEngine engine,
        IVerifyAdminCredentialHandler admin,
        IRestoreRecorder recorder,
        IUserContext userContext,
        IClock clock)
    {
        _engine = engine;
        _admin = admin;
        _recorder = recorder;
        _userContext = userContext;
        _clock = clock;
    }

    public async Task<Result<RestoredBackup>> ExecuteAsync(RestoreBackupCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.BackupFilePath) || !File.Exists(command.BackupFilePath))
        {
            return Result.Failure<RestoredBackup>("backup.restore.file-missing", "فایل پشتیبان پیدا نشد.");
        }

        var admin = await _admin
            .ExecuteAsync(new VerifyAdminCredentialCommand(command.AdminUsername, command.AdminPassword), cancellationToken)
            .ConfigureAwait(false);
        if (!admin.IsSuccess)
        {
            return Result.Failure<RestoredBackup>(
                "backup.restore.not-admin", admin.Error?.Message ?? "نام کاربری یا رمز مدیر درست نیست.");
        }

        StructuralCheckResult check;
        try
        {
            check = await _engine.RestoreAndCheckAsync(command.BackupFilePath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            check = new StructuralCheckResult(false, exception.Message);
        }

        if (!check.Success)
        {
            return Result.Failure<RestoredBackup>(
                "backup.restore.unusable",
                $"این فایل پشتیبان قابل بازیابی نیست؛ به اطلاعات فعلی دست زده نشد. ({check.Note})");
        }

        var now = _clock.UtcNow;
        // Never a name already on disk: restoring from a safety copy writes a new safety copy, and
        // two restores in one second must not overwrite the very file being restored from.
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
        var safetyPath = Path.Combine(command.SafetyBackupDirectory, $"before-restore-{now:yyyyMMdd-HHmmss}-{uniqueSuffix}.fbk");
        long safetySize;
        try
        {
            Directory.CreateDirectory(command.SafetyBackupDirectory);
            safetySize = await _engine.CreateBackupFileAsync(safetyPath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Result.Failure<RestoredBackup>(
                "backup.restore.safety-failed",
                $"نسخه‌ی ایمنی از اطلاعات فعلی گرفته نشد، پس بازیابی انجام نشد: {exception.Message}");
        }

        try
        {
            await _engine.ReplaceLiveDatabaseAsync(command.BackupFilePath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return await PutSafetyCopyBackAsync(safetyPath, exception, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await _recorder
                .RecordAsync(new RestoreRecord(_userContext.UserId, command.BackupFilePath, safetyPath, safetySize, now), cancellationToken)
                .ConfigureAwait(false);
            return Result.Success(new RestoredBackup(safetyPath, null));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The data is restored — that is what the user asked for, and undoing it now would be
            // the worse failure. Say plainly what did not happen.
            return Result.Success(new RestoredBackup(
                safetyPath, $"اطلاعات بازیابی شد ولی ثبت آن در سابقه ناموفق بود: {exception.Message}"));
        }
    }

    private async Task<Result<RestoredBackup>> PutSafetyCopyBackAsync(
        string safetyPath,
        Exception restoreFailure,
        CancellationToken cancellationToken)
    {
        try
        {
            await _engine.ReplaceLiveDatabaseAsync(safetyPath, cancellationToken).ConfigureAwait(false);
            return Result.Failure<RestoredBackup>(
                "backup.restore.failed-rolled-back",
                $"بازیابی ناموفق بود و اطلاعات قبلی سر جایش برگردانده شد: {restoreFailure.Message}");
        }
        catch (Exception rollbackFailure) when (rollbackFailure is not OperationCanceledException)
        {
            return Result.Failure<RestoredBackup>(
                "backup.restore.failed",
                $"بازیابی ناموفق بود و برگرداندن اطلاعات قبلی هم انجام نشد. نسخه‌ی ایمنی اطلاعات قبلی در «{safetyPath}» است؛ "
                + $"برنامه را نبندید و با پشتیبانی تماس بگیرید. ({restoreFailure.Message} / {rollbackFailure.Message})");
        }
    }
}
