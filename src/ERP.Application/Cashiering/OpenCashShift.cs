using ERP.Application.Audit;
using ERP.Application.Common;
using ERP.Domain.Cashiering;
using ERP.Domain.Common;
using ERP.Domain.Identity;
using ERP.Domain.Inventory;

namespace ERP.Application.Cashiering;

public sealed record OpenCashShiftCommand(WarehouseId WarehouseId, long OpeningCashTomans);

public interface IOpenCashShiftHandler
{
    Task<Result<CashShiftId>> ExecuteAsync(OpenCashShiftCommand command, CancellationToken cancellationToken);
}

/// <summary>«شیفت صندوق را شروع کرد» (milestone-1 §15 acceptance criterion 5) — one open shift per warehouse at a time, so cash sales always belong to exactly one shift's reconciliation.</summary>
public sealed class OpenCashShiftHandler : IOpenCashShiftHandler
{
    private readonly ICashShiftRepository _shifts;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;

    public OpenCashShiftHandler(
        ICashShiftRepository shifts,
        IAuditWriter audit,
        IUnitOfWork unitOfWork,
        IUserContext userContext,
        IClock clock)
    {
        _shifts = shifts;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _userContext = userContext;
        _clock = clock;
    }

    public async Task<Result<CashShiftId>> ExecuteAsync(OpenCashShiftCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var existing = await _shifts.GetOpenAsync(command.WarehouseId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return Result.Failure<CashShiftId>(
                "cashiering.shift.already-open",
                "یک شیفت صندوق از قبل باز است؛ اول همان را ببندید.");
        }

        try
        {
            var shift = CashShift.Open(
                command.WarehouseId,
                UserId.From(_userContext.UserId),
                Money.FromTomans(command.OpeningCashTomans),
                _clock.UtcNow);

            await _shifts.SaveAsync(shift, cancellationToken).ConfigureAwait(false);
            await _audit.WriteAsync(
                new AuditEntry(
                    Guid.NewGuid(),
                    _userContext.UserId,
                    "cashiering.shift.opened",
                    nameof(CashShift),
                    shift.Id.ToString(),
                    null,
                    $"opening={shift.OpeningCash.Rials}",
                    _clock.UtcNow),
                cancellationToken).ConfigureAwait(false);
            await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success(shift.Id);
        }
        catch (DomainException exception)
        {
            return Result.Failure<CashShiftId>("cashiering.shift.invalid", exception.Message);
        }
    }
}
