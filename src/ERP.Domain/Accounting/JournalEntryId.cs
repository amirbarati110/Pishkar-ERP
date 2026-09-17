using ERP.Domain.Common;

namespace ERP.Domain.Accounting;

public readonly record struct JournalEntryId
{
    private JournalEntryId(Guid value)
    {
        Value = value;
    }

    public Guid Value { get; }

    public static JournalEntryId New() => new(Guid.NewGuid());

    public static JournalEntryId From(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new DomainException("شناسه سند حسابداری معتبر نیست.");
        }

        return new JournalEntryId(value);
    }

    public override string ToString() => Value.ToString("D");
}
