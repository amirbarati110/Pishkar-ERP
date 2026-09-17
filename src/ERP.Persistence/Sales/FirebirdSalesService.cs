using ERP.Application.Common;
using ERP.Application.Sales;
using ERP.Domain.Sales;
using ERP.Persistence.Accounting;
using ERP.Persistence.Audit;
using ERP.Persistence.Catalog;
using ERP.Persistence.Customers;
using ERP.Persistence.Database;
using ERP.Persistence.Inventory;

namespace ERP.Persistence.Sales;

/// <summary>
/// Composition root for the Sales/POS use cases, mirroring
/// <see cref="Services.FirebirdRetailSetupService"/>'s pattern one call at a
/// time: each method opens its own <see cref="FirebirdUnitOfWork"/>, wires the
/// real handler with repositories bound to it, and lets that single transaction
/// commit or roll back as a unit. Kept as its own service (not folded into
/// FirebirdRetailSetupService) because Sales is its own bounded context with
/// its own owner module (source-of-truth rule #18).
/// </summary>
public sealed class FirebirdSalesService :
    IStartSaleHandler,
    IAddSaleLineHandler,
    IRemoveSaleLineHandler,
    ICompleteSaleHandler,
    IChangeSaleLineHandler,
    ISetSaleChargesHandler,
    ISetSaleCustomerHandler,
    IGetSaleDetailsHandler,
    IListSalesOfDayHandler,
    IListHeldSalesHandler,
    IBrowseProductsForSaleHandler,
    ICancelSaleHandler,
    IReadSaleProductsHandler,
    IGetLineEditInfoHandler,
    ISetSaleNoteHandler,
    IStartSaleCorrectionHandler,
    ICompleteSaleCorrectionHandler
{
    private readonly FirebirdConnectionFactory _connectionFactory;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;

    public FirebirdSalesService(
        FirebirdConnectionFactory connectionFactory,
        IUserContext userContext,
        IClock clock)
    {
        _connectionFactory = connectionFactory;
        _userContext = userContext;
        _clock = clock;
    }

    public async Task<Result<SaleId>> ExecuteAsync(
        StartSaleCommand command,
        CancellationToken cancellationToken)
    {
        await using var unitOfWork = await FirebirdUnitOfWork
            .CreateAsync(_connectionFactory, cancellationToken)
            .ConfigureAwait(false);
        return await new StartSaleHandler(new FirebirdSaleRepository(unitOfWork), unitOfWork, _clock)
            .ExecuteAsync(command, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Result<bool>> ExecuteAsync(
        AddSaleLineCommand command,
        CancellationToken cancellationToken)
    {
        await using var unitOfWork = await FirebirdUnitOfWork
            .CreateAsync(_connectionFactory, cancellationToken)
            .ConfigureAwait(false);
        return await new AddSaleLineHandler(
                new FirebirdSaleRepository(unitOfWork),
                new FirebirdProductRepository(unitOfWork),
                unitOfWork)
            .ExecuteAsync(command, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Result<bool>> ExecuteAsync(
        RemoveSaleLineCommand command,
        CancellationToken cancellationToken)
    {
        await using var unitOfWork = await FirebirdUnitOfWork
            .CreateAsync(_connectionFactory, cancellationToken)
            .ConfigureAwait(false);
        return await new RemoveSaleLineHandler(new FirebirdSaleRepository(unitOfWork), unitOfWork)
            .ExecuteAsync(command, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Result<CompletedSale>> ExecuteAsync(
        CompleteSaleCommand command,
        CancellationToken cancellationToken)
    {
        await using var unitOfWork = await FirebirdUnitOfWork
            .CreateAsync(_connectionFactory, cancellationToken)
            .ConfigureAwait(false);
        return await new CompleteSaleHandler(
                new FirebirdSaleRepository(unitOfWork),
                new FirebirdStockLedgerRepository(unitOfWork),
                new FirebirdSaleNumberGenerator(unitOfWork),
                new FirebirdCustomerRepository(unitOfWork),
                new FirebirdCustomerLedgerReader(unitOfWork),
                new FirebirdAuditWriter(unitOfWork),
                new FirebirdJournalEntryRepository(unitOfWork),
                unitOfWork,
                _userContext,
                _clock)
            .ExecuteAsync(command, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Result<SaleId>> ExecuteAsync(
        StartSaleCorrectionCommand command,
        CancellationToken cancellationToken)
    {
        await using var unitOfWork = await FirebirdUnitOfWork
            .CreateAsync(_connectionFactory, cancellationToken)
            .ConfigureAwait(false);
        return await new StartSaleCorrectionHandler(new FirebirdSaleRepository(unitOfWork), unitOfWork, _clock)
            .ExecuteAsync(command, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Result<CompletedSale>> ExecuteAsync(
        CompleteSaleCorrectionCommand command,
        CancellationToken cancellationToken)
    {
        await using var unitOfWork = await FirebirdUnitOfWork
            .CreateAsync(_connectionFactory, cancellationToken)
            .ConfigureAwait(false);
        return await new CompleteSaleCorrectionHandler(
                new FirebirdSaleRepository(unitOfWork),
                new FirebirdSaleNumberGenerator(unitOfWork),
                new FirebirdCustomerRepository(unitOfWork),
                new FirebirdCustomerLedgerReader(unitOfWork),
                new FirebirdAuditWriter(unitOfWork),
                new FirebirdJournalEntryRepository(unitOfWork),
                unitOfWork,
                _userContext,
                _clock)
            .ExecuteAsync(command, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Result<bool>> ExecuteAsync(
        ChangeSaleLineCommand command,
        CancellationToken cancellationToken)
    {
        await using var unitOfWork = await FirebirdUnitOfWork
            .CreateAsync(_connectionFactory, cancellationToken)
            .ConfigureAwait(false);
        return await new ChangeSaleLineHandler(
                new FirebirdSaleRepository(unitOfWork),
                new FirebirdProductRepository(unitOfWork),
                unitOfWork)
            .ExecuteAsync(command, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Result<bool>> ExecuteAsync(
        SetSaleChargesCommand command,
        CancellationToken cancellationToken)
    {
        await using var unitOfWork = await FirebirdUnitOfWork
            .CreateAsync(_connectionFactory, cancellationToken)
            .ConfigureAwait(false);
        return await new SetSaleChargesHandler(new FirebirdSaleRepository(unitOfWork), unitOfWork)
            .ExecuteAsync(command, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Result<bool>> ExecuteAsync(
        SetSaleCustomerCommand command,
        CancellationToken cancellationToken)
    {
        await using var unitOfWork = await FirebirdUnitOfWork
            .CreateAsync(_connectionFactory, cancellationToken)
            .ConfigureAwait(false);
        return await new SetSaleCustomerHandler(
                new FirebirdSaleRepository(unitOfWork),
                new FirebirdCustomerRepository(unitOfWork),
                unitOfWork)
            .ExecuteAsync(command, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Result<bool>> ExecuteAsync(
        SetSaleNoteCommand command,
        CancellationToken cancellationToken)
    {
        await using var unitOfWork = await FirebirdUnitOfWork
            .CreateAsync(_connectionFactory, cancellationToken)
            .ConfigureAwait(false);
        return await new SetSaleNoteHandler(new FirebirdSaleRepository(unitOfWork), unitOfWork)
            .ExecuteAsync(command, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Result<SaleDetails>> ExecuteAsync(
        GetSaleDetailsQuery query,
        CancellationToken cancellationToken)
    {
        await using var unitOfWork = await FirebirdUnitOfWork
            .CreateAsync(_connectionFactory, cancellationToken)
            .ConfigureAwait(false);
        return await new GetSaleDetailsHandler(
                new FirebirdSaleRepository(unitOfWork),
                new FirebirdSaleReadReader(unitOfWork),
                new FirebirdCustomerRepository(unitOfWork))
            .ExecuteAsync(query, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<SalesOfDay> ExecuteAsync(
        ListSalesOfDayQuery query,
        CancellationToken cancellationToken)
    {
        await using var unitOfWork = await FirebirdUnitOfWork
            .CreateAsync(_connectionFactory, cancellationToken)
            .ConfigureAwait(false);
        return await new ListSalesOfDayHandler(new FirebirdSaleReadReader(unitOfWork))
            .ExecuteAsync(query, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SaleListItem>> ExecuteAsync(
        ListHeldSalesQuery query,
        CancellationToken cancellationToken)
    {
        await using var unitOfWork = await FirebirdUnitOfWork
            .CreateAsync(_connectionFactory, cancellationToken)
            .ConfigureAwait(false);
        return await new ListHeldSalesHandler(new FirebirdSaleReadReader(unitOfWork))
            .ExecuteAsync(query, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<SaleProductListPage> ExecuteAsync(
        BrowseProductsForSaleQuery query,
        CancellationToken cancellationToken)
    {
        await using var unitOfWork = await FirebirdUnitOfWork
            .CreateAsync(_connectionFactory, cancellationToken)
            .ConfigureAwait(false);
        return await new BrowseProductsForSaleHandler(new FirebirdSaleReadReader(unitOfWork), _clock)
            .ExecuteAsync(query, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<Domain.Catalog.ProductId, SaleProductInfo>> ExecuteAsync(
        ReadSaleProductsQuery query,
        CancellationToken cancellationToken)
    {
        await using var unitOfWork = await FirebirdUnitOfWork
            .CreateAsync(_connectionFactory, cancellationToken)
            .ConfigureAwait(false);
        return await new ReadSaleProductsHandler(new FirebirdSaleReadReader(unitOfWork))
            .ExecuteAsync(query, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<LineEditInfo> ExecuteAsync(
        GetLineEditInfoQuery query,
        CancellationToken cancellationToken)
    {
        await using var unitOfWork = await FirebirdUnitOfWork
            .CreateAsync(_connectionFactory, cancellationToken)
            .ConfigureAwait(false);
        return await new GetLineEditInfoHandler(new FirebirdSaleReadReader(unitOfWork))
            .ExecuteAsync(query, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Result<bool>> ExecuteAsync(
        CancelSaleCommand command,
        CancellationToken cancellationToken)
    {
        await using var unitOfWork = await FirebirdUnitOfWork
            .CreateAsync(_connectionFactory, cancellationToken)
            .ConfigureAwait(false);
        return await new CancelSaleHandler(
                new FirebirdSaleRepository(unitOfWork),
                new FirebirdAuditWriter(unitOfWork),
                unitOfWork,
                _userContext,
                _clock)
            .ExecuteAsync(command, cancellationToken)
            .ConfigureAwait(false);
    }
}
