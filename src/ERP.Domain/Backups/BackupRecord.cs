using ERP.Domain.Common;

namespace ERP.Domain.Backups;

/// <summary>
/// «Backup محلی قابل اعتبارسنجی» (milestone-1 §14/§15, build order step 11).
/// The record of one backup file — a row here is not proof the backup is
/// good, only that one was produced; §16 «Backup فقط زمانی سالم محسوب
/// می‌شود که بازخوانی و بررسی ساختاری آن موفق باشد» means
/// <see cref="Status"/> stays <see cref="BackupVerificationStatus.NotVerified"/>
/// until someone actually restores it and checks it.
/// </summary>
public sealed class BackupRecord : Entity<BackupRecordId>
{
    private BackupRecord(BackupRecordId id, string filePath, long sizeBytes, DateTimeOffset createdAtUtc)
        : base(id)
    {
        FilePath = filePath;
        SizeBytes = sizeBytes;
        CreatedAtUtc = createdAtUtc;
        Status = BackupVerificationStatus.NotVerified;
    }

    public string FilePath { get; }

    public long SizeBytes { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public BackupVerificationStatus Status { get; private set; }

    public DateTimeOffset? VerifiedAtUtc { get; private set; }

    public string? VerificationNote { get; private set; }

    public static BackupRecord Create(string filePath, long sizeBytes, DateTimeOffset createdAtUtc)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new DomainException("مسیر فایل نسخه پشتیبان معتبر نیست.");
        }

        if (sizeBytes <= 0)
        {
            throw new DomainException("نسخه پشتیبان خالی است؛ چیزی برای اعتماد کردن نیست.");
        }

        return new BackupRecord(BackupRecordId.New(), filePath, sizeBytes, createdAtUtc);
    }

    internal static BackupRecord Rehydrate(
        BackupRecordId id,
        string filePath,
        long sizeBytes,
        DateTimeOffset createdAtUtc,
        BackupVerificationStatus status,
        DateTimeOffset? verifiedAtUtc,
        string? verificationNote)
    {
        return new BackupRecord(id, filePath, sizeBytes, createdAtUtc)
        {
            Status = status,
            VerifiedAtUtc = verifiedAtUtc,
            VerificationNote = verificationNote,
        };
    }

    /// <summary>Only meaningful once a real restore-and-check round trip has actually happened elsewhere (the Application-layer verify handler) — this method only records that outcome.</summary>
    public void MarkVerified(DateTimeOffset verifiedAtUtc, string note)
    {
        Status = BackupVerificationStatus.Verified;
        VerifiedAtUtc = verifiedAtUtc;
        VerificationNote = note;
    }

    public void MarkVerificationFailed(DateTimeOffset verifiedAtUtc, string reason)
    {
        Status = BackupVerificationStatus.Failed;
        VerifiedAtUtc = verifiedAtUtc;
        VerificationNote = reason;
    }
}
