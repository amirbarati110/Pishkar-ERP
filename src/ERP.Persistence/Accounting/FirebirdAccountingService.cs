using ERP.Application.Accounting;
using ERP.Persistence.Database;

namespace ERP.Persistence.Accounting;

/// <summary>Composition root for the Accounting module's read side — «مشاهده سند حسابداری» (§6.23) — one unit of work per call, same shape as the other Firebird*Service classes.</summary>
public sealed class FirebirdAccountingService : IGetSaleJournalEntryHandler
{
    private readonly FirebirdConnectionFactory _connectionFactory;

    public FirebirdAccountingService(FirebirdConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<JournalEntryView?> ExecuteAsync(GetSaleJournalEntryQuery query, CancellationToken cancellationToken)
    {
        await using var unitOfWork = await FirebirdUnitOfWork
            .CreateAsync(_connectionFactory, cancellationToken)
            .ConfigureAwait(false);
        return await new GetSaleJournalEntryHandler(new FirebirdJournalEntryRepository(unitOfWork))
            .ExecuteAsync(query, cancellationToken)
            .ConfigureAwait(false);
    }
}
