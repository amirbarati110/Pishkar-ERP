using ERP.Application.Common;
using ERP.Application.Sales;
using ERP.Domain.Cashiering;
using ERP.Domain.Common;
using ERP.Domain.Inventory;
using ERP.Domain.Sales;

namespace ERP.Application.Cashiering;

/// <summary>What the workbench's «وضعیت صندوق» tile shows — live, not just what was true at open.</summary>
public sealed record CashShiftStatusView(
    bool IsOpen,
    CashShiftId? ShiftId,
    DateTimeOffset? OpenedAtUtc,
    Money? OpeningCash,
    Money? CashSalesSoFar,
    Money? CashRefundsSoFar,
    Money? ExpectedCashSoFar);

public sealed record GetCashShiftStatusQuery(WarehouseId WarehouseId);

public interface IGetCashShiftStatusHandler
{
    Task<CashShiftStatusView> ExecuteAsync(GetCashShiftStatusQuery query, CancellationToken cancellationToken);
}

public sealed class GetCashShiftStatusHandler : IGetCashShiftStatusHandler
{
    private readonly ICashShiftRepository _shifts;
    private readonly ISaleReadReader _sales;
    private readonly IClock _clock;

    public GetCashShiftStatusHandler(ICashShiftRepository shifts, ISaleReadReader sales, IClock clock)
    {
        _shifts = shifts;
        _sales = sales;
        _clock = clock;
    }

    public async Task<CashShiftStatusView> ExecuteAsync(GetCashShiftStatusQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var shift = await _shifts.GetOpenAsync(query.WarehouseId, cancellationToken).ConfigureAwait(false);
        if (shift is null)
        {
            return new CashShiftStatusView(false, null, null, null, null, null, null);
        }

        var completed = await _sales
            .ListCompletedByWarehouseAsync(shift.WarehouseId, shift.OpenedAtUtc, _clock.UtcNow, cancellationToken)
            .ConfigureAwait(false);
        var cashSalesSoFar = Money.FromRials(completed
            .Where(item => item.PaymentMethod == PaymentMethod.Cash)
            .Sum(item => item.Amount?.Rials ?? 0));

        var returns = await _sales
            .ListReturnsByWarehouseAsync(shift.WarehouseId, shift.OpenedAtUtc, _clock.UtcNow, cancellationToken)
            .ConfigureAwait(false);
        var cashRefundsSoFar = Money.FromRials(returns
            .Where(item => item.RefundMethod == PaymentMethod.Cash)
            .Sum(item => item.Refund.Rials));

        return new CashShiftStatusView(
            true,
            shift.Id,
            shift.OpenedAtUtc,
            shift.OpeningCash,
            cashSalesSoFar,
            cashRefundsSoFar,
            Money.FromRials(Math.Max(0, shift.OpeningCash.Rials + cashSalesSoFar.Rials - cashRefundsSoFar.Rials)));
    }
}
