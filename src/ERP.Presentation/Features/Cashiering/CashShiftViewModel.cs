using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ERP.Application.Cashiering;
using ERP.Domain.Cashiering;
using ERP.Domain.Inventory;
using ERP.Presentation.Features.Sales;

namespace ERP.Presentation.Features.Cashiering;

/// <summary>
/// «شیفت صندوق پایه» (milestone-1 §8/§15 acceptance criterion 5). One screen,
/// two states: no shift open shows the opening form; an open shift shows its
/// live numbers and the closing form. Sales itself is not gated on this yet
/// (deliberate limitation, recorded in the checklist) — a cashier can still
/// sell with no shift open; this page is where they choose to track one.
/// </summary>
public sealed partial class CashShiftViewModel : ObservableObject
{
    private readonly IOpenCashShiftHandler _open;
    private readonly ICloseCashShiftHandler _close;
    private readonly IGetCashShiftStatusHandler _status;
    private readonly WarehouseId _warehouseId;

    private CashShiftId? _openShiftId;

    public CashShiftViewModel(
        IOpenCashShiftHandler open,
        ICloseCashShiftHandler close,
        IGetCashShiftStatusHandler status,
        WarehouseId warehouseId)
    {
        _open = open;
        _close = close;
        _status = status;
        _warehouseId = warehouseId;
    }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    [ObservableProperty]
    public partial string OpenedAtText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string OpeningCashText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CashSalesSoFarText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CashRefundsSoFarText { get; set; } = string.Empty;

    /// <summary>«دریافت از مشتری» paid in cash at this till — cash in the drawer that is not a sale.</summary>
    [ObservableProperty]
    public partial string CashReceiptsSoFarText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ExpectedCashSoFarText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string OpeningCashInput { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? OpenError { get; set; }

    [ObservableProperty]
    public partial string CountedCashInput { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CloseNoteInput { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? CloseError { get; set; }

    [ObservableProperty]
    public partial bool IsCloseSummaryOpen { get; set; }

    [ObservableProperty]
    public partial string CloseSummaryText { get; set; } = string.Empty;

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        try
        {
            var status = await _status.ExecuteAsync(new GetCashShiftStatusQuery(_warehouseId), cancellationToken).ConfigureAwait(true);
            IsOpen = status.IsOpen;
            _openShiftId = status.ShiftId;

            if (status.IsOpen)
            {
                OpenedAtText = SalesText.ClockTime(status.OpenedAtUtc!.Value);
                OpeningCashText = $"{SalesText.Tomans(status.OpeningCash!.Value)} تومان";
                CashSalesSoFarText = $"{SalesText.Tomans(status.CashSalesSoFar!.Value)} تومان";
                CashRefundsSoFarText = $"{SalesText.Tomans(status.CashRefundsSoFar!.Value)} تومان";
                CashReceiptsSoFarText = $"{SalesText.Tomans(status.CashReceiptsSoFar ?? Domain.Common.Money.Zero)} تومان";
                ExpectedCashSoFarText = $"{SalesText.Tomans(status.ExpectedCashSoFar!.Value)} تومان";
                CountedCashInput = string.Empty;
                CloseNoteInput = string.Empty;
                CloseError = null;
            }
            else
            {
                OpeningCashInput = string.Empty;
                OpenError = null;
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task OpenAsync()
    {
        OpenError = null;
        var openingRials = SalesText.ParseTomansToRials(OpeningCashInput);
        if (openingRials is not { } rials || rials < 0)
        {
            OpenError = "مبلغ ابتدای شیفت را درست وارد کنید.";
            return;
        }

        IsLoading = true;
        try
        {
            var result = await _open.ExecuteAsync(
                new OpenCashShiftCommand(_warehouseId, rials / 10), CancellationToken.None).ConfigureAwait(true);
            if (!result.IsSuccess)
            {
                OpenError = result.Error?.Message ?? "شروع شیفت ناموفق بود.";
                return;
            }

            await LoadAsync(CancellationToken.None).ConfigureAwait(true);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task CloseAsync()
    {
        CloseError = null;
        if (_openShiftId is not { } shiftId)
        {
            return;
        }

        var countedRials = SalesText.ParseTomansToRials(CountedCashInput);
        if (countedRials is not { } rials || rials < 0)
        {
            CloseError = "مبلغ شمارش‌شده را درست وارد کنید.";
            return;
        }

        IsLoading = true;
        try
        {
            var note = string.IsNullOrWhiteSpace(CloseNoteInput) ? null : CloseNoteInput.Trim();
            var result = await _close.ExecuteAsync(
                new CloseCashShiftCommand(shiftId, rials / 10, note), CancellationToken.None).ConfigureAwait(true);
            if (!result.IsSuccess)
            {
                CloseError = result.Error?.Message ?? "بستن شیفت ناموفق بود.";
                return;
            }

            var summary = result.Value!;
            var varianceText = summary.VarianceRials switch
            {
                0 => "دقیقاً برابر انتظار بود.",
                > 0 => $"{SalesText.Tomans(summary.VarianceRials)} تومان بیشتر از انتظار بود.",
                _ => $"{SalesText.Tomans(-summary.VarianceRials)} تومان کمتر از انتظار بود.",
            };
            CloseSummaryText =
                $"فروش نقدی این شیفت: {SalesText.Tomans(summary.CashSalesDuringShift)} تومان\n"
                + $"دریافت نقدی از مشتریان: {SalesText.Tomans(summary.CashReceiptsDuringShift)} تومان\n"
                + $"پول برگشتی به مشتری (مرجوعی و اصلاح فاکتور): {SalesText.Tomans(summary.CashRefundsDuringShift)} تومان\n"
                + $"مبلغ مورد انتظار: {SalesText.Tomans(summary.ExpectedCash)} تومان\n"
                + $"مبلغ شمارش‌شده: {SalesText.Tomans(summary.CountedCash)} تومان\n"
                + varianceText;
            IsCloseSummaryOpen = true;

            await LoadAsync(CancellationToken.None).ConfigureAwait(true);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void DismissCloseSummary() => IsCloseSummaryOpen = false;
}
