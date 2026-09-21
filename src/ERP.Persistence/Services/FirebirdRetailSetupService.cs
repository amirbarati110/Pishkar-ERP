using ERP.Application.Catalog;
using ERP.Application.Common;
using ERP.Application.Importing;
using ERP.Application.Inventory;
using ERP.Domain.Catalog;
using ERP.Persistence.Audit;
using ERP.Persistence.Catalog;
using ERP.Persistence.Database;
using ERP.Persistence.Inventory;

namespace ERP.Persistence.Services;

public sealed class FirebirdRetailSetupService :
    ICreateCategoryHandler,
    ICreateProductHandler,
    IUpdateProductHandler,
    IArchiveProductHandler,
    IReceiveOpeningStockHandler,
    ICommitProductImportHandler
{
    private readonly FirebirdConnectionFactory _connectionFactory;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;

    public FirebirdRetailSetupService(
        FirebirdConnectionFactory connectionFactory,
        IUserContext userContext,
        IClock clock)
    {
        _connectionFactory = connectionFactory;
        _userContext = userContext;
        _clock = clock;
    }

    public async Task<Result<CategoryId>> ExecuteAsync(
        CreateCategoryCommand command,
        CancellationToken cancellationToken)
    {
        await using var unitOfWork = await FirebirdUnitOfWork
            .CreateAsync(_connectionFactory, cancellationToken)
            .ConfigureAwait(false);
        return await new CreateCategoryHandler(
                new FirebirdCategoryRepository(unitOfWork),
                CreateAuditWriter(unitOfWork),
                unitOfWork,
                _userContext,
                _clock)
            .ExecuteAsync(command, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Result<ProductId>> ExecuteAsync(
        CreateProductCommand command,
        CancellationToken cancellationToken)
    {
        await using var unitOfWork = await FirebirdUnitOfWork
            .CreateAsync(_connectionFactory, cancellationToken)
            .ConfigureAwait(false);
        return await new CreateProductHandler(
                new FirebirdProductRepository(unitOfWork),
                CreateAuditWriter(unitOfWork),
                unitOfWork,
                _userContext,
                _clock)
            .ExecuteAsync(command, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Result<bool>> ExecuteAsync(
        UpdateProductCommand command,
        CancellationToken cancellationToken)
    {
        await using var unitOfWork = await FirebirdUnitOfWork
            .CreateAsync(_connectionFactory, cancellationToken)
            .ConfigureAwait(false);
        return await new UpdateProductHandler(
                new FirebirdProductRepository(unitOfWork),
                CreateAuditWriter(unitOfWork),
                unitOfWork,
                _userContext,
                _clock)
            .ExecuteAsync(command, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Result<bool>> ExecuteAsync(
        ArchiveProductCommand command,
        CancellationToken cancellationToken)
    {
        await using var unitOfWork = await FirebirdUnitOfWork
            .CreateAsync(_connectionFactory, cancellationToken)
            .ConfigureAwait(false);
        return await new ArchiveProductHandler(
                new FirebirdProductRepository(unitOfWork),
                CreateAuditWriter(unitOfWork),
                unitOfWork,
                _userContext,
                _clock)
            .ExecuteAsync(command, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Result<bool>> ExecuteAsync(
        ReceiveOpeningStockCommand command,
        CancellationToken cancellationToken)
    {
        await using var unitOfWork = await FirebirdUnitOfWork
            .CreateAsync(_connectionFactory, cancellationToken)
            .ConfigureAwait(false);
        return await new ReceiveOpeningStockHandler(
                new FirebirdStockLedgerRepository(unitOfWork),
                CreateAuditWriter(unitOfWork),
                unitOfWork,
                _userContext,
                _clock)
            .ExecuteAsync(command, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Result<int>> ExecuteAsync(
        CommitProductImportCommand command,
        CancellationToken cancellationToken)
    {
        await using var unitOfWork = await FirebirdUnitOfWork
            .CreateAsync(_connectionFactory, cancellationToken)
            .ConfigureAwait(false);
        return await new CommitProductImportHandler(
                new FirebirdProductRepository(unitOfWork),
                new FirebirdStockLedgerRepository(unitOfWork),
                CreateAuditWriter(unitOfWork),
                unitOfWork,
                _userContext,
                _clock)
            .ExecuteAsync(command, cancellationToken)
            .ConfigureAwait(false);
    }

    private static FirebirdAuditWriter CreateAuditWriter(FirebirdUnitOfWork unitOfWork)
    {
        return new FirebirdAuditWriter(unitOfWork);
    }
}
