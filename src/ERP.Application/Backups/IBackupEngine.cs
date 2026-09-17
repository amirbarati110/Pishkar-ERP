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
}
