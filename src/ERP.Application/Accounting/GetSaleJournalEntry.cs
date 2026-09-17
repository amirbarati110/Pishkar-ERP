using ERP.Domain.Accounting;
using ERP.Domain.Common;
using ERP.Domain.Sales;

namespace ERP.Application.Accounting;

/// <summary>One line as the manager's viewer shows it — the raw domain line, nothing computed.</summary>
public sealed record JournalLineView(AccountCode Account, Money Debit, Money Credit);

/// <summary>«مشاهده سند حسابداری» (§6.23) — never shown to the cashier, only to «مدیر/حسابدار» who opens it deliberately.</summary>
public sealed record JournalEntryView(JournalSourceType SourceType, DateTimeOffset PostedAtUtc, IReadOnlyList<JournalLineView> Lines);

public sealed record GetSaleJournalEntryQuery(SaleId SaleId);

public interface IGetSaleJournalEntryHandler
{
    Task<JournalEntryView?> ExecuteAsync(GetSaleJournalEntryQuery query, CancellationToken cancellationToken);
}

public sealed class GetSaleJournalEntryHandler : IGetSaleJournalEntryHandler
{
    private readonly IJournalEntryRepository _journal;

    public GetSaleJournalEntryHandler(IJournalEntryRepository journal)
    {
        _journal = journal;
    }

    public async Task<JournalEntryView?> ExecuteAsync(GetSaleJournalEntryQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var sourceId = query.SaleId.ToString();
        var entry = await _journal.GetBySourceAsync(JournalSourceType.Sale, sourceId, cancellationToken).ConfigureAwait(false)
            ?? await _journal.GetBySourceAsync(JournalSourceType.SaleCorrection, sourceId, cancellationToken).ConfigureAwait(false);

        return entry is null
            ? null
            : new JournalEntryView(
                entry.SourceType,
                entry.PostedAtUtc,
                entry.Lines.Select(line => new JournalLineView(line.Account, line.Debit, line.Credit)).ToList());
    }
}
