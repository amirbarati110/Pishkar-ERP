using ERP.Application.Audit;
using ERP.Application.Common;
using ERP.Application.Customers;
using ERP.Application.Sales;
using ERP.Domain.Cashiering;
using ERP.Domain.Common;
using ERP.Domain.Identity;
using ERP.Domain.Sales;

namespace ERP.Application.Cashiering;

public sealed record CloseCashShiftCommand(CashShiftId ShiftId, long CountedCashTomans, string? Note);

/// <summary>What the closing screen shows once the shift is reconciled — every figure the cashier needs to understand the variance, not just its final number.</summary>
public sealed record CashShiftCloseSummary(
    Money OpeningCash,
    Money CashSalesDuringShift,
    Money CashRefundsDuringShift,
    Money ExpectedCash,
    Money CountedCash,
    long VarianceRials,
    Money CashReceiptsDuringShift = default);

public interface ICloseCashShiftHandler
{
    Task<Result<CashShiftCloseSummary>> ExecuteAsync(CloseCashShiftCommand command, CancellationToken cancellationToken);
}

/// <summary>
/// Counts the drawer against what cash sales say should be there. The cash
/// total is read fresh from Sales at close time — never trusted from the
/// caller — because §5's module boundary rule means CashShift itself cannot
/// query the SALE table directly; only this Application-layer handler is
/// allowed to combine the two modules' data.
/// </summary>
public sealed class CloseCashShiftHandler : ICloseCashShiftHandler
{
    private readonly ICashShiftRepository _shifts;
    private readonly ISaleReadReader _sales;
    private readonly ICustomerCashReceiptReader _receipts;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;

    public CloseCashShiftHandler(
        ICashShiftRepository shifts,
        ISaleReadReader sales,
        ICustomerCashReceiptReader receipts,
        IAuditWriter audit,
        IUnitOfWork unitOfWork,
        IUserContext userContext,
        IClock clock)
    {
        _shifts = shifts;
        _sales = sales;
        _receipts = receipts;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _userContext = userContext;
        _clock = clock;
    }

    public async Task<Result<CashShiftCloseSummary>> ExecuteAsync(CloseCashShiftCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var shift = await _shifts.GetByIdAsync(command.ShiftId, cancellationToken).ConfigureAwait(false);
        if (shift is null)
        {
            return Result.Failure<CashShiftCloseSummary>("cashiering.shift.not-found", "شیفت صندوق پیدا نشد.");
        }

        var closedAtUtc = _clock.UtcNow;
        var cash = await ShiftCash
            .ReadAsync(_sales, _receipts, shift.WarehouseId, shift.OpenedAtUtc, closedAtUtc, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            shift.Close(
                Money.FromTomans(command.CountedCashTomans),
                cash.Sales,
                cash.HandedBack,
                cash.Receipts,
                UserId.From(_userContext.UserId),
                closedAtUtc,
                command.Note);

            await _shifts.SaveAsync(shift, cancellationToken).ConfigureAwait(false);
            await _audit.WriteAsync(
                new AuditEntry(
                    Guid.NewGuid(),
                    _userContext.UserId,
                    "cashiering.shift.closed",
                    nameof(CashShift),
                    shift.Id.ToString(),
                    null,
                    $"counted={shift.CountedCash!.Value.Rials};expected={shift.ExpectedCash!.Value.Rials};variance={shift.Variance}",
                    _clock.UtcNow),
                cancellationToken).ConfigureAwait(false);
            await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success(new CashShiftCloseSummary(
                shift.OpeningCash,
                cash.Sales,
                cash.HandedBack,
                shift.ExpectedCash!.Value,
                shift.CountedCash!.Value,
                shift.Variance!.Value,
                cash.Receipts));
        }
        catch (DomainException exception)
        {
            return Result.Failure<CashShiftCloseSummary>("cashiering.shift.invalid", exception.Message);
        }
    }
}
