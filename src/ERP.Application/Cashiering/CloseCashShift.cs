using ERP.Application.Audit;
using ERP.Application.Common;
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
    long VarianceRials);

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
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;

    public CloseCashShiftHandler(
        ICashShiftRepository shifts,
        ISaleReadReader sales,
        IAuditWriter audit,
        IUnitOfWork unitOfWork,
        IUserContext userContext,
        IClock clock)
    {
        _shifts = shifts;
        _sales = sales;
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
        var completed = await _sales
            .ListCompletedByWarehouseAsync(shift.WarehouseId, shift.OpenedAtUtc, closedAtUtc, cancellationToken)
            .ConfigureAwait(false);
        var cashSalesRials = completed
            .Where(item => item.PaymentMethod == PaymentMethod.Cash)
            .Sum(item => item.Amount?.Rials ?? 0);
        var cashSales = Money.FromRials(cashSalesRials);

        // Cash handed back for returns left the drawer too, so the shift is
        // told about both: expected cash = opening + cash sales − cash refunds.
        var returns = await _sales
            .ListReturnsByWarehouseAsync(shift.WarehouseId, shift.OpenedAtUtc, closedAtUtc, cancellationToken)
            .ConfigureAwait(false);
        var cashRefunds = Money.FromRials(returns
            .Where(item => item.RefundMethod == PaymentMethod.Cash)
            .Sum(item => item.Refund.Rials));

        try
        {
            shift.Close(
                Money.FromTomans(command.CountedCashTomans),
                cashSales,
                cashRefunds,
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
                cashSales,
                cashRefunds,
                shift.ExpectedCash!.Value,
                shift.CountedCash!.Value,
                shift.Variance!.Value));
        }
        catch (DomainException exception)
        {
            return Result.Failure<CashShiftCloseSummary>("cashiering.shift.invalid", exception.Message);
        }
    }
}
