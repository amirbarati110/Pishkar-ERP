using ERP.Application.Audit;
using ERP.Application.Common;
using ERP.Domain.Common;
using ERP.Domain.Inventory;

namespace ERP.Application.Inventory;

// «لیست انبارها» (checklist «س»): reading the list, creating and editing a warehouse, and taking
// one out of use. «حذف» archives — refused for the last active warehouse (the app always needs
// somewhere to sell from), one that still has stock, or one with an open cash shift.

public interface IWarehouseRepository
{
    Task<Warehouse?> GetByIdAsync(WarehouseId warehouseId, CancellationToken cancellationToken);

    Task<Warehouse?> FindByNameAsync(string name, CancellationToken cancellationToken);

    Task AddAsync(Warehouse warehouse, CancellationToken cancellationToken);

    Task UpdateAsync(Warehouse warehouse, CancellationToken cancellationToken);

    Task<int> CountActiveAsync(CancellationToken cancellationToken);

    /// <summary>Any product still has a positive layer in this warehouse.</summary>
    Task<bool> HasStockAsync(WarehouseId warehouseId, CancellationToken cancellationToken);

    /// <summary>A shift on this warehouse is still open (not yet closed).</summary>
    Task<bool> HasOpenCashShiftAsync(WarehouseId warehouseId, CancellationToken cancellationToken);
}

public sealed record WarehouseListQuery(string? Term);

public sealed record WarehouseListRow(WarehouseId Id, string Name, string? Address);

public interface IWarehouseListReader
{
    Task<IReadOnlyList<WarehouseListRow>> ListAsync(WarehouseListQuery query, CancellationToken cancellationToken);
}

public interface IListWarehousesHandler
{
    Task<IReadOnlyList<WarehouseListRow>> ExecuteAsync(WarehouseListQuery query, CancellationToken cancellationToken);
}

public sealed class ListWarehousesHandler : IListWarehousesHandler
{
    private readonly IWarehouseListReader _reader;

    public ListWarehousesHandler(IWarehouseListReader reader)
    {
        _reader = reader;
    }

    public Task<IReadOnlyList<WarehouseListRow>> ExecuteAsync(WarehouseListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var term = string.IsNullOrWhiteSpace(query.Term) ? null : query.Term.Trim();
        return _reader.ListAsync(query with { Term = term }, cancellationToken);
    }
}

public sealed record CreateWarehouseCommand(string Name, string? Address);

public interface ICreateWarehouseHandler
{
    Task<Result<WarehouseId>> ExecuteAsync(CreateWarehouseCommand command, CancellationToken cancellationToken);
}

public sealed class CreateWarehouseHandler : ICreateWarehouseHandler
{
    private readonly IWarehouseRepository _warehouses;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;

    public CreateWarehouseHandler(
        IWarehouseRepository warehouses,
        IAuditWriter audit,
        IUnitOfWork unitOfWork,
        IUserContext userContext,
        IClock clock)
    {
        _warehouses = warehouses;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _userContext = userContext;
        _clock = clock;
    }

    public async Task<Result<WarehouseId>> ExecuteAsync(CreateWarehouseCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        try
        {
            var warehouse = Warehouse.Create(command.Name, command.Address);

            if (await _warehouses.FindByNameAsync(warehouse.Name, cancellationToken).ConfigureAwait(false) is not null)
            {
                return Result.Failure<WarehouseId>("inventory.warehouse.duplicate-name", "انباری با همین نام از قبل وجود دارد.");
            }

            await _warehouses.AddAsync(warehouse, cancellationToken).ConfigureAwait(false);
            await _audit.WriteAsync(
                new AuditEntry(
                    Guid.NewGuid(),
                    _userContext.UserId,
                    "inventory.warehouse.created",
                    nameof(Warehouse),
                    warehouse.Id.ToString(),
                    null,
                    Describe(warehouse),
                    _clock.UtcNow),
                cancellationToken).ConfigureAwait(false);
            await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success(warehouse.Id);
        }
        catch (DomainException exception)
        {
            return Result.Failure<WarehouseId>("inventory.warehouse.invalid", exception.Message);
        }
        catch (DataConflictException exception)
        {
            return Result.Failure<WarehouseId>(exception.Code, exception.Message);
        }
    }

    internal static string Describe(Warehouse warehouse) => $"{warehouse.Name} | {warehouse.Address}";
}

public sealed record UpdateWarehouseCommand(WarehouseId WarehouseId, string Name, string? Address);

public interface IUpdateWarehouseHandler
{
    Task<Result<bool>> ExecuteAsync(UpdateWarehouseCommand command, CancellationToken cancellationToken);
}

public sealed class UpdateWarehouseHandler : IUpdateWarehouseHandler
{
    private readonly IWarehouseRepository _warehouses;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;

    public UpdateWarehouseHandler(
        IWarehouseRepository warehouses,
        IAuditWriter audit,
        IUnitOfWork unitOfWork,
        IUserContext userContext,
        IClock clock)
    {
        _warehouses = warehouses;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _userContext = userContext;
        _clock = clock;
    }

    public async Task<Result<bool>> ExecuteAsync(UpdateWarehouseCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        try
        {
            var warehouse = await _warehouses.GetByIdAsync(command.WarehouseId, cancellationToken).ConfigureAwait(false);
            if (warehouse is null)
            {
                return Result.Failure<bool>("inventory.warehouse.not-found", "این انبار پیدا نشد؛ ممکن است حذف شده باشد.");
            }

            if (warehouse.Status == WarehouseStatus.Archived)
            {
                return Result.Failure<bool>("inventory.warehouse.archived", "این انبار حذف شده و دیگر قابل ویرایش نیست.");
            }

            var before = CreateWarehouseHandler.Describe(warehouse);
            warehouse.Update(command.Name, command.Address);

            var sameName = await _warehouses.FindByNameAsync(warehouse.Name, cancellationToken).ConfigureAwait(false);
            if (sameName is not null && sameName.Id != warehouse.Id)
            {
                return Result.Failure<bool>("inventory.warehouse.duplicate-name", "انباری با همین نام از قبل وجود دارد.");
            }

            await _warehouses.UpdateAsync(warehouse, cancellationToken).ConfigureAwait(false);
            await _audit.WriteAsync(
                new AuditEntry(
                    Guid.NewGuid(),
                    _userContext.UserId,
                    "inventory.warehouse.updated",
                    nameof(Warehouse),
                    warehouse.Id.ToString(),
                    before,
                    CreateWarehouseHandler.Describe(warehouse),
                    _clock.UtcNow),
                cancellationToken).ConfigureAwait(false);
            await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success(true);
        }
        catch (DomainException exception)
        {
            return Result.Failure<bool>("inventory.warehouse.invalid", exception.Message);
        }
        catch (DataConflictException exception)
        {
            return Result.Failure<bool>(exception.Code, exception.Message);
        }
    }
}

public sealed record ArchiveWarehouseCommand(WarehouseId WarehouseId);

public interface IArchiveWarehouseHandler
{
    Task<Result<bool>> ExecuteAsync(ArchiveWarehouseCommand command, CancellationToken cancellationToken);
}

/// <summary>
/// Takes a warehouse out of use. Refused for the last active one (every screen that sells or
/// receives stock needs somewhere to point at), one that still holds stock (it would silently
/// vanish from every kardex and total), or one with an open cash shift (closing it needs its
/// warehouse to still exist as a live choice). Doing it twice is not an error.
/// </summary>
public sealed class ArchiveWarehouseHandler : IArchiveWarehouseHandler
{
    private readonly IWarehouseRepository _warehouses;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;

    public ArchiveWarehouseHandler(
        IWarehouseRepository warehouses,
        IAuditWriter audit,
        IUnitOfWork unitOfWork,
        IUserContext userContext,
        IClock clock)
    {
        _warehouses = warehouses;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _userContext = userContext;
        _clock = clock;
    }

    public async Task<Result<bool>> ExecuteAsync(ArchiveWarehouseCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var warehouse = await _warehouses.GetByIdAsync(command.WarehouseId, cancellationToken).ConfigureAwait(false);
        if (warehouse is null)
        {
            return Result.Failure<bool>("inventory.warehouse.not-found", "این انبار پیدا نشد؛ ممکن است قبلاً حذف شده باشد.");
        }

        if (warehouse.Status == WarehouseStatus.Archived)
        {
            return Result.Success(true);
        }

        if (await _warehouses.CountActiveAsync(cancellationToken).ConfigureAwait(false) <= 1)
        {
            return Result.Failure<bool>("inventory.warehouse.last-active", "این تنها انبار فعال است و حذف نمی‌شود؛ برنامه همیشه به یک انبار نیاز دارد.");
        }

        if (await _warehouses.HasStockAsync(warehouse.Id, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<bool>("inventory.warehouse.has-stock", "این انبار هنوز موجودی دارد و حذف نمی‌شود؛ اول موجودی را خالی کنید.");
        }

        if (await _warehouses.HasOpenCashShiftAsync(warehouse.Id, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<bool>("inventory.warehouse.has-open-shift", "این انبار یک شیفت صندوق باز دارد و حذف نمی‌شود؛ اول شیفت را ببندید.");
        }

        warehouse.Archive();
        await _warehouses.UpdateAsync(warehouse, cancellationToken).ConfigureAwait(false);
        await _audit.WriteAsync(
            new AuditEntry(
                Guid.NewGuid(),
                _userContext.UserId,
                "inventory.warehouse.archived",
                nameof(Warehouse),
                warehouse.Id.ToString(),
                null,
                warehouse.Name,
                _clock.UtcNow),
            cancellationToken).ConfigureAwait(false);
        await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(true);
    }
}
