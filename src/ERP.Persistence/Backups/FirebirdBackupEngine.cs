using ERP.Application.Backups;
using ERP.Persistence.Database;
using FirebirdSql.Data.FirebirdClient;
using FirebirdSql.Data.Services;

namespace ERP.Persistence.Backups;

/// <summary>
/// The real gbak-level work behind «Backup محلی قابل اعتبارسنجی» (§15.10/§16):
/// <see cref="FbBackup"/>/<see cref="FbRestore"/> from the FirebirdClient
/// provider already referenced everywhere else in this project — no extra
/// package, no external `gbak.exe` process to shell out to.
/// </summary>
public sealed class FirebirdBackupEngine : IBackupEngine
{
    private readonly FirebirdOptions _options;

    public FirebirdBackupEngine(FirebirdOptions options)
    {
        _options = options;
    }

    public async Task<long> CreateBackupFileAsync(string destinationFilePath, CancellationToken cancellationToken)
    {
        var backup = new FbBackup
        {
            ConnectionString = BuildConnectionString(_options.DatabasePath),
            Verbose = false,
            Options = FbBackupFlags.IgnoreLimbo,
        };
        backup.BackupFiles.Add(new FbBackupFile(destinationFilePath));
        await backup.ExecuteAsync(cancellationToken).ConfigureAwait(false);

        // FbBackup's Services API attachment to the source database is a
        // separate native handle from the app's own pooled FbConnections; it
        // is released asynchronously by the embedded engine after
        // ExecuteAsync returns, not synchronously with it.
        await ReleaseFileAsync(_options.DatabasePath, cancellationToken).ConfigureAwait(false);

        return new FileInfo(destinationFilePath).Length;
    }

    public async Task<StructuralCheckResult> RestoreAndCheckAsync(string backupFilePath, CancellationToken cancellationToken)
    {
        var restoredDatabasePath = Path.Combine(Path.GetTempPath(), $"pishkar-verify-{Guid.NewGuid():N}.fdb");

        try
        {
            var restore = new FbRestore
            {
                ConnectionString = BuildConnectionString(restoredDatabasePath),
                Verbose = false,
                Options = FbRestoreFlags.Create | FbRestoreFlags.Replace,
            };
            restore.BackupFiles.Add(new FbBackupFile(backupFilePath));
            await restore.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            await ReleaseFileAsync(backupFilePath, cancellationToken).ConfigureAwait(false);

            // The restored .fdb itself needs the same wait: FbRestore.ExecuteAsync
            // returning does not guarantee the engine has finished making the
            // freshly-created file connectable yet (found by re-running this
            // test a handful of times: intermittent "database ... shutdown" and
            // "I/O error during ReadFile" — a genuine race, not test flakiness —
            // because only backupFilePath, never restoredDatabasePath, was
            // waited on before the very first connection attempt below).
            await ReleaseFileAsync(restoredDatabasePath, cancellationToken).ConfigureAwait(false);
            var result = await CheckRestoredDatabaseAsync(restoredDatabasePath, cancellationToken).ConfigureAwait(false);
            var released = await ReleaseFileAsync(restoredDatabasePath, cancellationToken).ConfigureAwait(false);
            return released
                ? result
                : result with { Note = result.Note + " (هشدار: فایل موقت بازیابی هنوز آزاد نشده بود؛ ممکن است در پوشه‌ی موقت سیستم باقی بماند.)" };
        }
        finally
        {
            if (File.Exists(restoredDatabasePath))
            {
                try
                {
                    File.Delete(restoredDatabasePath);
                }
                catch (IOException)
                {
                    // Best-effort cleanup — the warning above already told the
                    // caller this could happen; failing verification over a
                    // leftover temp file would be the wrong trade-off.
                }
            }
        }
    }

    /// <summary>
    /// «بازخوانی و بررسی ساختاری» (§16): open the just-restored database and
    /// confirm its own migration history — the same table every other health
    /// check reads — came back intact.
    ///
    /// Retries a genuinely-open connection a few times: the OS-level wait in
    /// <see cref="ReleaseFileAsync"/> only proves the file handle is free, not
    /// that the Firebird engine has finished making the page structure of a
    /// freshly-restored file connectable — a real connection attempt is the
    /// only true test of that, and it can still transiently fail right after
    /// restore with errors as different as "database ... shutdown" and
    /// "I/O error during ReadFile" (both seen in practice, not hypothetical).
    /// </summary>
    private async Task<StructuralCheckResult> CheckRestoredDatabaseAsync(string restoredDatabasePath, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await TryCheckRestoredDatabaseAsync(restoredDatabasePath, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (attempt < 10 && exception is not OperationCanceledException)
            {
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<StructuralCheckResult> TryCheckRestoredDatabaseAsync(string restoredDatabasePath, CancellationToken cancellationToken)
    {
        await using var connection = new FbConnection(BuildConnectionString(restoredDatabasePath));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = new FbCommand("SELECT COUNT(*) FROM SCHEMA_MIGRATIONS", connection);
        var appliedMigrations = Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            System.Globalization.CultureInfo.InvariantCulture);

        return new StructuralCheckResult(
            true, $"بازیابی موفق بود؛ {appliedMigrations} ردیف در تاریخچه‌ی migration بازخوانی شد.");
    }

    /// <summary>
    /// Waits (briefly, bounded) for the Firebird embedded engine to actually
    /// let go of <paramref name="databasePath"/> after a Services API
    /// attachment (<see cref="FbBackup"/>/<see cref="FbRestore"/>) used it —
    /// <see cref="FbConnection.ClearAllPools"/> only clears the app's own
    /// pooled connections, not this separate native handle. Returns false
    /// (never throws) if the file is still locked after every attempt, so a
    /// slow release never turns a genuinely successful backup/restore into a
    /// failure — callers that care fold the outcome into their own result
    /// instead of it vanishing silently.
    /// </summary>
    private static async Task<bool> ReleaseFileAsync(string databasePath, CancellationToken cancellationToken)
    {
        FbConnection.ClearAllPools();

        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                await using var probe = File.Open(databasePath, FileMode.Open, FileAccess.Read, FileShare.None);
                return true;
            }
            catch (IOException)
            {
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }
        }

        return false;
    }

    private string BuildConnectionString(string databasePath)
    {
        var builder = new FbConnectionStringBuilder
        {
            Database = databasePath,
            UserID = _options.UserName,
            Password = _options.Password,
            ServerType = FbServerType.Embedded,
            ClientLibrary = _options.ClientLibraryPath,
            Charset = "UTF8",
            Dialect = 3,
            Pooling = false,
        };

        return builder.ConnectionString;
    }
}
