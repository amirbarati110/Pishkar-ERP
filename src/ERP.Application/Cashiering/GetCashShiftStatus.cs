using ERP.Application.Common;
using ERP.Application.Customers;
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
    Money? ExpectedCashSoFar,
    Money? CashReceiptsSoFar = null);

public sealed record GetCashShiftStatusQuery(WarehouseId WarehouseId);

public interface IGetCashShiftStatusHandler
{
    Task<CashShiftStatusView> ExecuteAsync(GetCashShiftStatusQuery query, CancellationToken cancellationToken);
}

public sealed class GetCashShiftStatusHandler : IGetCashShiftStatusHandler
{
    private readonly ICashShiftRepository _shifts;
    private readonly ISaleReadReader _sales;
    private readonly ICustomerCashReceiptReader _receipts;
    private readonly IClock _clock;

    public GetCashShiftStatusHandler(
        ICashShiftRepository shifts,
        ISaleReadReader sales,
        ICustomerCashReceiptReader receipts,
        IClock clock)
    {
        _shifts = shifts;
        _sales = sales;
        _receipts = receipts;
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

        var cash = await ShiftCash
            .ReadAsync(_sales, _receipts, shift.WarehouseId, shift.OpenedAtUtc, _clock.UtcNow, cancellationToken)
            .ConfigureAwait(false);

        return new CashShiftStatusView(
            true,
            shift.Id,
            shift.OpenedAtUtc,
            shift.OpeningCash,
            cash.Sales,
            cash.HandedBack,
            CashShift.Expected(shift.OpeningCash, cash.Sales, cash.Receipts, cash.HandedBack),
            cash.Receipts);
    }
}
