using ERP.Domain.Common;

namespace ERP.Domain.Accounting;

/// <summary>
/// «Business Operation سند تولید می‌کند. کاربر عملیاتی Debit/Credit نمی‌بیند»
/// (milestone-1 §10.5). A cashier never builds one of these by hand — every
/// entry is produced automatically from a completed sale (see
/// <c>ERP.Application.Accounting.SaleJournalEntryFactory</c>) and is only
/// ever read back by «مدیر/حسابدار» (§6.23), never edited.
/// </summary>
public sealed class JournalEntry : Entity<JournalEntryId>
{
    private readonly List<JournalLine> _lines = [];

    private JournalEntry(JournalEntryId id, JournalSourceType sourceType, string sourceId, DateTimeOffset postedAtUtc)
        : base(id)
    {
        SourceType = sourceType;
        SourceId = sourceId;
        PostedAtUtc = postedAtUtc;
    }

    public JournalSourceType SourceType { get; }

    /// <summary>The originating record's own id (e.g. a <c>SaleId</c>), as text — Accounting does not reference Sales' types directly (module boundary, §5).</summary>
    public string SourceId { get; }

    public DateTimeOffset PostedAtUtc { get; }

    public IReadOnlyList<JournalLine> Lines => _lines.AsReadOnly();

    /// <summary>Double-entry's one hard rule: every entry balances, or it never exists.</summary>
    public static JournalEntry Create(
        JournalSourceType sourceType,
        string sourceId,
        DateTimeOffset postedAtUtc,
        IReadOnlyList<JournalLine> lines)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            throw new DomainException("سند حسابداری باید به یک رکورد منبع وصل باشد.");
        }

        if (lines.Count < 2)
        {
            throw new DomainException("سند حسابداری باید حداقل دو سطر داشته باشد.");
        }

        var totalDebit = lines.Sum(line => line.Debit.Rials);
        var totalCredit = lines.Sum(line => line.Credit.Rials);
        if (totalDebit != totalCredit)
        {
            throw new DomainException("سند حسابداری نامتوازن است؛ جمع بدهکار باید برابر جمع بستانکار باشد.");
        }

        var entry = new JournalEntry(JournalEntryId.New(), sourceType, sourceId, postedAtUtc);
        entry._lines.AddRange(lines);
        return entry;
    }

    internal static JournalEntry Rehydrate(
        JournalEntryId id,
        JournalSourceType sourceType,
        string sourceId,
        DateTimeOffset postedAtUtc,
        IEnumerable<JournalLine> lines)
    {
        var entry = new JournalEntry(id, sourceType, sourceId, postedAtUtc);
        entry._lines.AddRange(lines);
        return entry;
    }
}
