using ERP.Application.Common;
using ERP.Application.Customers;
using ERP.Domain.Customers;
using ERP.Persistence.Accounting;
using ERP.Persistence.Audit;
using ERP.Persistence.Database;

namespace ERP.Persistence.Customers;

/// <summary>
/// Composition root for the Customers module, one unit of work per call —
/// the same shape as <see cref="Sales.FirebirdSalesService"/>. Its own service
/// because Customers is its own owner module (source-of-truth rule 18).
/// </summary>
public sealed class FirebirdCustomerService :
    IQuickCreateCustomerHandler,
    ISearchCustomersHandler,
    IGetCustomerAccountHandler,
    IRecordCustomerPaymentHandler,
    IListCustomersHandler,
    ICreateCustomerHandler,
    IUpdateCustomerHandler,
    IArchiveCustomerHandler
{
    private readonly FirebirdConnectionFactory _connectionFactory;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;

    public FirebirdCustomerService(
        FirebirdConnectionFactory connectionFactory,
        IUserContext userContext,
        IClock clock)
    {
        _connectionFactory = connectionFactory;
        _userContext = userContext;
        _clock = clock;
    }

    public async Task<Result<CustomerId>> ExecuteAsync(
        QuickCreateCustomerCommand command,
        CancellationToken cancellationToken)
    {
        await using var unitOfWork = await FirebirdUnitOfWork
            .CreateAsync(_connectionFactory, cancellationToken)
            .ConfigureAwait(false);
        return await new QuickCreateCustomerHandler(
                new FirebirdCustomerRepository(unitOfWork),
                new FirebirdAuditWriter(unitOfWork),
                unitOfWork,
                _userContext,
                _clock)
            .ExecuteAsync(command, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Result<CustomerAccountSummary>> ExecuteAsync(
        GetCustomerAccountQuery query,
        CancellationToken cancellationToken)
    {
        await using var unitOfWork = await FirebirdUnitOfWork
            .CreateAsync(_connectionFactory, cancellationToken)
            .ConfigureAwait(false);
        return await new GetCustomerAccountHandler(
                new FirebirdCustomerRepository(unitOfWork),
                new FirebirdCustomerLedgerReader(unitOfWork))
            .ExecuteAsync(query, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Result<Guid>> ExecuteAsync(
        RecordCustomerPaymentCommand command,
        CancellationToken cancellationToken)
    {
        await using var unitOfWork = await FirebirdUnitOfWork
            .CreateAsync(_connectionFactory, cancellationToken)
            .ConfigureAwait(false);
        return await new RecordCustomerPaymentHandler(
                new FirebirdCustomerRepository(unitOfWork),
                new FirebirdCustomerPaymentRepository(unitOfWork),
                new FirebirdAuditWriter(unitOfWork),
                new FirebirdJournalEntryRepository(unitOfWork),
                unitOfWork,
                _userContext,
                _clock)
            .ExecuteAsync(command, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<CustomerListPage> ExecuteAsync(CustomerListQuery query, CancellationToken cancellationToken)
    {
        return new ListCustomersHandler(new FirebirdCustomerListReader(_connectionFactory))
            .ExecuteAsync(query, cancellationToken);
    }

    public async Task<Result<CustomerId>> ExecuteAsync(CreateCustomerCommand command, CancellationToken cancellationToken)
    {
        await using var unitOfWork = await FirebirdUnitOfWork
            .CreateAsync(_connectionFactory, cancellationToken)
            .ConfigureAwait(false);
        return await new CreateCustomerHandler(
                new FirebirdCustomerRepository(unitOfWork),
                new FirebirdAuditWriter(unitOfWork),
                new FirebirdJournalEntryRepository(unitOfWork),
                unitOfWork,
                _userContext,
                _clock)
            .ExecuteAsync(command, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Result<bool>> ExecuteAsync(UpdateCustomerCommand command, CancellationToken cancellationToken)
    {
        await using var unitOfWork = await FirebirdUnitOfWork
            .CreateAsync(_connectionFactory, cancellationToken)
            .ConfigureAwait(false);
        return await new UpdateCustomerHandler(
                new FirebirdCustomerRepository(unitOfWork),
                new FirebirdAuditWriter(unitOfWork),
                unitOfWork,
                _userContext,
                _clock)
            .ExecuteAsync(command, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Result<bool>> ExecuteAsync(ArchiveCustomerCommand command, CancellationToken cancellationToken)
    {
        await using var unitOfWork = await FirebirdUnitOfWork
            .CreateAsync(_connectionFactory, cancellationToken)
            .ConfigureAwait(false);
        return await new ArchiveCustomerHandler(
                new FirebirdCustomerRepository(unitOfWork),
                new FirebirdCustomerLedgerReader(unitOfWork),
                new FirebirdAuditWriter(unitOfWork),
                unitOfWork,
                _userContext,
                _clock)
            .ExecuteAsync(command, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<IReadOnlyList<CustomerSearchResult>> ExecuteAsync(
        SearchCustomersQuery query,
        CancellationToken cancellationToken)
    {
        return new SearchCustomersHandler(new FirebirdCustomerSearchReader(_connectionFactory))
            .ExecuteAsync(query, cancellationToken);
    }
}
