using ERP.Application.Common;
using ERP.Application.Customers;
using ERP.Domain.Customers;
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
    ISearchCustomersHandler
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

    public Task<IReadOnlyList<CustomerSearchResult>> ExecuteAsync(
        SearchCustomersQuery query,
        CancellationToken cancellationToken)
    {
        return new SearchCustomersHandler(new FirebirdCustomerSearchReader(_connectionFactory))
            .ExecuteAsync(query, cancellationToken);
    }
}
