namespace ERP.Application.Backups;

public sealed record StructuralCheckResult(bool Success, string Note);

/// <summary>
/// The actual gbak-level work (Firebird Services API) — only a Persistence
/// implementation exists, since «سالم بودن» a backup means genuinely
/// restoring it and checking the result (§16), not just checking a file
/// exists.
/// </summary>
public interface IBackupEngine
{
    /// <returns>The written file's size in bytes.</returns>
    Task<long> CreateBackupFileAsync(string destinationFilePath, CancellationToken cancellationToken);

    /// <summary>Restores <paramref name="backupFilePath"/> into a throwaway database, checks it structurally, then deletes the throwaway file either way.</summary>
    Task<StructuralCheckResult> RestoreAndCheckAsync(string backupFilePath, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces the app's own live database with the contents of <paramref name="backupFilePath"/>.
    /// Destructive: only <see cref="RestoreBackupHandler"/> calls it, and only after the file has
    /// passed <see cref="RestoreAndCheckAsync"/> and a safety backup of the live data exists.
    /// </summary>
    Task ReplaceLiveDatabaseAsync(string backupFilePath, CancellationToken cancellationToken);
}
