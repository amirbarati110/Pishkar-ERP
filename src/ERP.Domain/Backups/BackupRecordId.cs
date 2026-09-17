using ERP.Domain.Common;

namespace ERP.Domain.Backups;

public readonly record struct BackupRecordId
{
    private BackupRecordId(Guid value)
    {
        Value = value;
    }

    public Guid Value { get; }

    public static BackupRecordId New() => new(Guid.NewGuid());

    public static BackupRecordId From(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new DomainException("شناسه نسخه پشتیبان معتبر نیست.");
        }

        return new BackupRecordId(value);
    }

    public override string ToString() => Value.ToString("D");
}
