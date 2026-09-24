using ERP.Application.Catalog;
using ERP.Application.Common;
using ERP.Application.Identity;
using ERP.Application.Importing;
using ERP.Application.Inventory;
using ERP.Domain.Catalog;
using ERP.Domain.Identity;
using ERP.Domain.Inventory;
using ERP.Persistence.Accounting;
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
    IGenerateProductBarcodeHandler,
    IReceiveOpeningStockHandler,
    ICommitProductImportHandler,
    IListWarehousesHandler,
    ICreateWarehouseHandler,
    IUpdateWarehouseHandler,
    IArchiveWarehouseHandler
{
    private readonly FirebirdConnectionFactory _connectionFactory;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;
    private readonly IAccessChecker _access;

    public FirebirdRetailSetupService(
        FirebirdConnectionFactory connectionFactory,
        IUserContext userContext,
        IClock clock,
        IAccessChecker access)
    {
        _connectionFactory = connectionFactory;
        _userContext = userContext;
        _clock = clock;
        _access = access;
    }

    public async Task<Result<CategoryId>> ExecuteAsync(
        CreateCategoryCommand command,
        CancellationToken cancellationToken)
    {
        if (await _access.CheckAsync(AccessRight.ManageCatalog, cancellationToken).ConfigureAwait(false) is { } denied)
        {
            return Result.Failure<CategoryId>(denied.Code, denied.Message);
        }

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
        if (await _access.CheckAsync(AccessRight.ManageCatalog, cancellationToken).ConfigureAwait(false) is { } denied)
        {
            return Result.Failure<ProductId>(denied.Code, denied.Message);
        }

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

    public async Task<Result<string>> ExecuteAsync(CancellationToken cancellationToken)
    {
        if (await _access.CheckAsync(AccessRight.ManageCatalog, cancellationToken).ConfigureAwait(false) is { } denied)
        {
            return Result.Failure<string>(denied.Code, denied.Message);
        }

        await using var unitOfWork = await FirebirdUnitOfWork
            .CreateAsync(_connectionFactory, cancellationToken)
            .ConfigureAwait(false);
        return await new GenerateProductBarcodeHandler(
                new FirebirdInternalBarcodeSequence(unitOfWork),
                new FirebirdProductRepository(unitOfWork))
            .ExecuteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Result<bool>> ExecuteAsync(
        UpdateProductCommand command,
        CancellationToken cancellationToken)
    {
        if (await _access.CheckAsync(AccessRight.ManageCatalog, cancellationToken).ConfigureAwait(false) is { } denied)
        {
            return Result.Failure<bool>(denied.Code, denied.Message);
        }

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
        if (await _access.CheckAsync(AccessRight.ManageCatalog, cancellationToken).ConfigureAwait(false) is { } denied)
        {
            return Result.Failure<bool>(denied.Code, denied.Message);
        }

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
        if (await _access.CheckAsync(AccessRight.ManageStock, cancellationToken).ConfigureAwait(false) is { } denied)
        {
            return Result.Failure<bool>(denied.Code, denied.Message);
        }

        await using var unitOfWork = await FirebirdUnitOfWork
            .CreateAsync(_connectionFactory, cancellationToken)
            .ConfigureAwait(false);
        return await new ReceiveOpeningStockHandler(
                new FirebirdStockLedgerRepository(unitOfWork),
                CreateAuditWriter(unitOfWork),
                new FirebirdJournalEntryRepository(unitOfWork),
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
        if (await _access.CheckAsync(AccessRight.ManageStock, cancellationToken).ConfigureAwait(false) is { } denied)
        {
            return Result.Failure<int>(denied.Code, denied.Message);
        }

        await using var unitOfWork = await FirebirdUnitOfWork
            .CreateAsync(_connectionFactory, cancellationToken)
            .ConfigureAwait(false);
        return await new CommitProductImportHandler(
                new FirebirdProductRepository(unitOfWork),
                new FirebirdStockLedgerRepository(unitOfWork),
                CreateAuditWriter(unitOfWork),
                new FirebirdJournalEntryRepository(unitOfWork),
                unitOfWork,
                _userContext,
                _clock)
            .ExecuteAsync(command, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<IReadOnlyList<WarehouseListRow>> ExecuteAsync(WarehouseListQuery query, CancellationToken cancellationToken)
    {
        return new ListWarehousesHandler(new FirebirdWarehouseListReader(_connectionFactory))
            .ExecuteAsync(query, cancellationToken);
    }

    public async Task<Result<WarehouseId>> ExecuteAsync(CreateWarehouseCommand command, CancellationToken cancellationToken)
    {
        if (await _access.CheckAsync(AccessRight.ManageStock, cancellationToken).ConfigureAwait(false) is { } denied)
        {
            return Result.Failure<WarehouseId>(denied.Code, denied.Message);
        }

        await using var unitOfWork = await FirebirdUnitOfWork
            .CreateAsync(_connectionFactory, cancellationToken)
            .ConfigureAwait(false);
        return await new CreateWarehouseHandler(
                new FirebirdWarehouseRepository(unitOfWork),
                CreateAuditWriter(unitOfWork),
                unitOfWork,
                _userContext,
                _clock)
            .ExecuteAsync(command, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Result<bool>> ExecuteAsync(UpdateWarehouseCommand command, CancellationToken cancellationToken)
    {
        if (await _access.CheckAsync(AccessRight.ManageStock, cancellationToken).ConfigureAwait(false) is { } denied)
        {
            return Result.Failure<bool>(denied.Code, denied.Message);
        }

        await using var unitOfWork = await FirebirdUnitOfWork
            .CreateAsync(_connectionFactory, cancellationToken)
            .ConfigureAwait(false);
        return await new UpdateWarehouseHandler(
                new FirebirdWarehouseRepository(unitOfWork),
                CreateAuditWriter(unitOfWork),
                unitOfWork,
                _userContext,
                _clock)
            .ExecuteAsync(command, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Result<bool>> ExecuteAsync(ArchiveWarehouseCommand command, CancellationToken cancellationToken)
    {
        if (await _access.CheckAsync(AccessRight.ManageStock, cancellationToken).ConfigureAwait(false) is { } denied)
        {
            return Result.Failure<bool>(denied.Code, denied.Message);
        }

        await using var unitOfWork = await FirebirdUnitOfWork
            .CreateAsync(_connectionFactory, cancellationToken)
            .ConfigureAwait(false);
        return await new ArchiveWarehouseHandler(
                new FirebirdWarehouseRepository(unitOfWork),
                CreateAuditWriter(unitOfWork),
                unitOfWork,
                _userContext,
                _clock,
                FirebirdDatabaseBootstrapper.MainWarehouseId)
            .ExecuteAsync(command, cancellationToken)
            .ConfigureAwait(false);
    }

    private static FirebirdAuditWriter CreateAuditWriter(FirebirdUnitOfWork unitOfWork)
    {
        return new FirebirdAuditWriter(unitOfWork);
    }
}
