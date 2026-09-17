using ERP.Domain.Backups;
using ERP.Domain.Common;

namespace ERP.Domain.Tests.Backups;

public sealed class BackupRecordTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 9, 17, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CreateStartsAsNotVerified()
    {
        var record = BackupRecord.Create(@"D:\backups\backup-1.fbk", 12_345, CreatedAt);

        Assert.Equal(BackupVerificationStatus.NotVerified, record.Status);
        Assert.Null(record.VerifiedAtUtc);
        Assert.Equal(12_345, record.SizeBytes);
    }

    [Fact]
    public void CreateRejectsABlankPath()
    {
        var exception = Assert.Throws<DomainException>(() => BackupRecord.Create(" ", 1000, CreatedAt));

        Assert.Equal("مسیر فایل نسخه پشتیبان معتبر نیست.", exception.Message);
    }

    [Fact]
    public void CreateRejectsAnEmptyFile()
    {
        var exception = Assert.Throws<DomainException>(() => BackupRecord.Create(@"D:\backups\empty.fbk", 0, CreatedAt));

        Assert.Equal("نسخه پشتیبان خالی است؛ چیزی برای اعتماد کردن نیست.", exception.Message);
    }

    [Fact]
    public void MarkVerifiedRecordsWhenAndWhy()
    {
        var record = BackupRecord.Create(@"D:\backups\backup-1.fbk", 12_345, CreatedAt);
        var verifiedAt = CreatedAt.AddMinutes(5);

        record.MarkVerified(verifiedAt, "۴ جدول اصلی بازیابی و شمارش شدند.");

        Assert.Equal(BackupVerificationStatus.Verified, record.Status);
        Assert.Equal(verifiedAt, record.VerifiedAtUtc);
        Assert.Equal("۴ جدول اصلی بازیابی و شمارش شدند.", record.VerificationNote);
    }

    [Fact]
    public void MarkVerificationFailedRecordsTheReason()
    {
        var record = BackupRecord.Create(@"D:\backups\backup-1.fbk", 12_345, CreatedAt);
        var verifiedAt = CreatedAt.AddMinutes(5);

        record.MarkVerificationFailed(verifiedAt, "بازیابی فایل شکست خورد.");

        Assert.Equal(BackupVerificationStatus.Failed, record.Status);
        Assert.Equal("بازیابی فایل شکست خورد.", record.VerificationNote);
    }
}
