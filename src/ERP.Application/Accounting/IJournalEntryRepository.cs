using ERP.Domain.Accounting;

namespace ERP.Application.Accounting;

public interface IJournalEntryRepository
{
    Task SaveAsync(JournalEntry entry, CancellationToken cancellationToken);

    /// <summary>The entry posted for one source record (e.g. a sale) — at most one per source in milestone 1 (a correction posts its own separate entry, it never edits the original's).</summary>
    Task<JournalEntry?> GetBySourceAsync(JournalSourceType sourceType, string sourceId, CancellationToken cancellationToken);
}
