using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ERP.Application.Sales;
using ERP.Domain.Common;
using ERP.Domain.Customers;
using ERP.Domain.Sales;

namespace ERP.Presentation.Features.Sales;

// «مرجوعی» و «تعویض» (§6.25–6.26): پیدا کردن فاکتور ← انتخاب سطر و تعداد ← مقصد کالا ← روش بازپرداخت ← ثبت.
// این کلاس هیچ مبلغی حساب نمی‌کند؛ مبلغ بازپرداخت را همان کدی می‌دهد که در لحظه‌ی ثبت هم استفاده می‌شود.
public sealed partial class SalesWorkspaceViewModel
{
    private SaleId? _returnSaleId;
    private CustomerId? _returnCustomerId;
    private int _returnPreviewVersion;
    private Money _doneRefund = Money.Zero;

    public ObservableCollection<ReturnLineRow> ReturnLines { get; } = [];

    [ObservableProperty]
    public partial bool IsReturnOpen { get; set; }

    /// <summary>True once an invoice has been found and its rows are on screen.</summary>
    [ObservableProperty]
    public partial bool HasReturnInvoice { get; set; }

    /// <summary>True after a successful «ثبت مرجوعی» — the window then shows the result and the exchange button.</summary>
    [ObservableProperty]
    public partial bool IsReturnDone { get; set; }

    [ObservableProperty]
    public partial string ReturnNumberInput { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ReturnInvoiceHeaderText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ReturnReasonText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ReturnRefundText { get; set; } = "۰";

    [ObservableProperty]
    public partial PaymentMethod ReturnRefundMethod { get; set; } = PaymentMethod.Cash;

    /// <summary>
    /// The invoice has a «خدمات/هزینه» that can still be given back — only then is the
    /// «خدمات/هزینه هم پس داده شود» choice shown (user decision 1405/07/02).
    /// </summary>
    [ObservableProperty]
    public partial bool CanRefundServiceCharge { get; set; }

    /// <summary>The service charge with its VAT, as the choice shows it — from the server, not computed here.</summary>
    [ObservableProperty]
    public partial string ServiceChargeRefundText { get; set; } = string.Empty;

    /// <summary>The cashier's choice; off by default — a delivery that happened is normally kept.</summary>
    [ObservableProperty]
    public partial bool RefundServiceCharge { get; set; }

    partial void OnRefundServiceChargeChanged(bool value) => _ = RefreshReturnPreviewAsync();

    /// <summary>«کم شدن از بدهی» only makes sense when the invoice has a customer whose account it can reduce.</summary>
    [ObservableProperty]
    public partial bool CanRefundToDebt { get; set; }

    [ObservableProperty]
    public partial string? ReturnError { get; set; }

    [ObservableProperty]
    public partial string ReturnDoneText { get; set; } = string.Empty;

    private string _doneNumberText = string.Empty;

    /// <summary>Something is picked to give back: a goods row, or the service charge.</summary>
    public bool HasReturnSelection => ReturnLines.Any(row => row.IsSelected) || RefundServiceCharge;

    /// <summary>The rows, method and reason can be changed only until the return is registered — after that it is a posted document.</summary>
    public bool IsReturnEditable => HasReturnInvoice && !IsReturnDone;

    partial void OnHasReturnInvoiceChanged(bool value) => OnPropertyChanged(nameof(IsReturnEditable));

    partial void OnIsReturnDoneChanged(bool value) => OnPropertyChanged(nameof(IsReturnEditable));

    /// <summary>«مرجوعی» in the bottom bar: starts empty, asking for the invoice number.</summary>
    [RelayCommand]
    private void OpenReturn()
    {
        ResetReturn();
        IsReturnOpen = true;
    }

    /// <summary>«مرجوعی» on a row of «فاکتورهای امروز» — no need to type the number.</summary>
    [RelayCommand]
    private Task OpenReturnForRowAsync(InvoiceListRow? row) => row is null ? Task.CompletedTask : RunAsync(async () =>
    {
        ResetReturn();
        IsReturnOpen = true;
        IsInvoiceListOpen = false;
        await LoadReturnableAsync(row.Item.SaleId);
    });

    [RelayCommand]
    private Task FindReturnInvoiceAsync() => RunAsync(async () =>
    {
        ReturnError = null;
        var latin = PersianNumber.DigitsToLatin(ReturnNumberInput.Trim());
        if (!long.TryParse(latin, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number))
        {
            ReturnError = "شماره فاکتور را درست وارد کنید.";
            return;
        }

        var found = await _backend.FindReturnInvoice.ExecuteAsync(new FindSaleForReturnQuery(number), CancellationToken.None);
        if (!found.IsSuccess)
        {
            ReturnError = found.Error?.Message;
            return;
        }

        await LoadReturnableAsync(found.Value);
    });

    private async Task LoadReturnableAsync(SaleId saleId)
    {
        var result = await _backend.ReturnableSale.ExecuteAsync(new GetReturnableSaleQuery(saleId), CancellationToken.None);
        if (!result.IsSuccess)
        {
            ReturnError = result.Error?.Message;
            return;
        }

        var sale = result.Value!;
        _returnSaleId = sale.SaleId;
        _returnCustomerId = sale.CustomerId;
        CanRefundToDebt = sale.CustomerId is not null;
        ReturnRefundMethod = sale.PaymentMethod == PaymentMethod.Card
            ? PaymentMethod.Card
            : sale.PaymentMethod == PaymentMethod.Credit && sale.CustomerId is not null
                ? PaymentMethod.Credit
                : PaymentMethod.Cash;
        CanRefundServiceCharge = !sale.RefundableServiceCharge.IsEmpty;
        ServiceChargeRefundText = CanRefundServiceCharge
            ? $"{SalesText.Tomans(sale.RefundableServiceCharge.Total)} تومان"
            : string.Empty;
        RefundServiceCharge = false;
        ReturnInvoiceHeaderText = $"فاکتور {sale.Number.ToPersianString()} · {SalesText.PaymentMethodName(sale.PaymentMethod)} · {SalesText.Tomans(sale.Total)} تومان";

        ReturnLines.Clear();
        foreach (var line in sale.Lines)
        {
            var row = new ReturnLineRow(line);
            row.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(ReturnLineRow.DispositionIndex))
                {
                    _ = RefreshReturnPreviewAsync();
                }
            };
            ReturnLines.Add(row);
        }

        HasReturnInvoice = true;
        ReturnError = null;
        await RefreshReturnPreviewAsync();
    }

    [RelayCommand]
    private Task IncreaseReturnQuantityAsync(ReturnLineRow? row) => ChangeReturnQuantityAsync(row, +1);

    [RelayCommand]
    private Task DecreaseReturnQuantityAsync(ReturnLineRow? row) => ChangeReturnQuantityAsync(row, -1);

    private async Task ChangeReturnQuantityAsync(ReturnLineRow? row, int direction)
    {
        if (row is null || !row.CanReturn)
        {
            return;
        }

        var step = Math.Min(1m, row.Line.RemainingQuantity);
        row.Quantity = direction > 0
            ? Math.Min(row.Line.RemainingQuantity, row.Quantity + step)
            : Math.Max(0m, row.Quantity - step);
        await RefreshReturnPreviewAsync();
    }

    /// <summary>«برگشت همه» — every row that still has something left, all of it.</summary>
    [RelayCommand]
    private async Task ReturnAllAsync()
    {
        foreach (var row in ReturnLines.Where(item => item.CanReturn))
        {
            row.Quantity = row.Line.RemainingQuantity;
        }

        await RefreshReturnPreviewAsync();
    }

    [RelayCommand]
    private void SelectReturnRefundMethod(PaymentMethod method)
    {
        if (method == PaymentMethod.Credit && !CanRefundToDebt)
        {
            return;
        }

        ReturnRefundMethod = method;
    }

    private List<ReturnLineRequest> PickedReturnLines() => ReturnLines
        .Where(row => row.IsSelected)
        .Select(row => new ReturnLineRequest(row.Line.ProductId, row.Quantity, row.Disposition))
        .ToList();

    /// <summary>
    /// Asks the server what the picked rows would refund. Not run through
    /// <see cref="RunAsync"/> on purpose: it must keep up with every tap on
    /// + / − even while another action is finishing, so a stale answer is
    /// discarded by version instead of blocked.
    /// </summary>
    private async Task RefreshReturnPreviewAsync()
    {
        OnPropertyChanged(nameof(HasReturnSelection));

        var version = ++_returnPreviewVersion;
        var picked = PickedReturnLines();
        if (_returnSaleId is not { } saleId || (picked.Count == 0 && !RefundServiceCharge))
        {
            ReturnRefundText = "۰";
            return;
        }

        var preview = await _backend.PreviewReturn.ExecuteAsync(
            new PreviewSaleReturnQuery(saleId, picked, RefundServiceCharge), CancellationToken.None);
        if (version != _returnPreviewVersion)
        {
            return;
        }

        if (preview.IsSuccess)
        {
            ReturnRefundText = SalesText.Tomans(preview.Value!.Refund);
            ReturnError = null;
        }
        else
        {
            ReturnRefundText = "۰";
            ReturnError = preview.Error?.Message;
        }
    }

    [RelayCommand]
    private Task ConfirmReturnAsync() => RunAsync(async () =>
    {
        if (_returnSaleId is not { } saleId)
        {
            return;
        }

        var picked = PickedReturnLines();
        if (picked.Count == 0 && !RefundServiceCharge)
        {
            ReturnError = "هیچ کالایی برای مرجوعی انتخاب نشده است.";
            return;
        }

        var result = await _backend.CompleteReturn.ExecuteAsync(
            new CompleteSaleReturnCommand(saleId, picked, ReturnRefundMethod, ReturnReasonText, RefundServiceCharge),
            CancellationToken.None);
        if (!result.IsSuccess)
        {
            ReturnError = result.Error?.Message ?? "مرجوعی ثبت نشد.";
            return;
        }

        _doneRefund = result.Value!.Refund;
        _doneNumberText = result.Value.Number.ToPersianString();
        ReturnDoneText = $"مرجوعی شماره {_doneNumberText} ثبت شد — {SalesText.Tomans(result.Value.Refund)} تومان "
            + ReturnRefundMethod switch
            {
                PaymentMethod.Card => "به کارت مشتری برگردانده شود.",
                PaymentMethod.Credit => "از بدهی مشتری کم شد.",
                _ => "از صندوق به مشتری پرداخت شود.",
            };
        ReturnError = null;
        IsReturnDone = true;

        await LoadTilesAsync(CancellationToken.None);
    });

    /// <summary>
    /// «تعویض» (§6.26): the return is already done; this opens a fresh invoice
    /// for the same customer and remembers what was refunded, so the payment
    /// window can show the difference. Two complete documents, no special
    /// accounting — the cashier just nets the money in the drawer.
    /// </summary>
    [RelayCommand]
    private Task StartExchangeAsync() => RunAsync(async () =>
    {
        await AddNewTabCoreAsync(CancellationToken.None);
        var tab = Tabs[^1];

        if (_returnCustomerId is { } customerId)
        {
            var assigned = await _backend.SetCustomer.ExecuteAsync(
                new SetSaleCustomerCommand(tab.SaleId, customerId), CancellationToken.None);
            if (!assigned.IsSuccess)
            {
                ShowNotice(assigned.Error?.Message ?? "مشتری فاکتور جایگزین انتخاب نشد.", isError: true);
            }

            await RefreshInvoiceAsync(tab, CancellationToken.None);
        }

        tab.ExchangeRefundRials = _doneRefund.Rials;
        tab.ExchangeReturnNumberText = _doneNumberText;
        CurrentTab = tab;
        OnPropertyChanged(nameof(TabPositionText));
        IsReturnOpen = false;
        ShowNotice($"فاکتور جایگزین برای مرجوعی {_doneNumberText} باز شد؛ مابه‌التفاوت هنگام پرداخت نشان داده می‌شود.");
    });

    [RelayCommand]
    private void CloseReturn() => IsReturnOpen = false;

    private void ResetReturn()
    {
        _returnSaleId = null;
        _returnCustomerId = null;
        _returnPreviewVersion++;
        ReturnLines.Clear();
        HasReturnInvoice = false;
        IsReturnDone = false;
        ReturnNumberInput = string.Empty;
        ReturnInvoiceHeaderText = string.Empty;
        ReturnReasonText = string.Empty;
        ReturnRefundText = "۰";
        ReturnRefundMethod = PaymentMethod.Cash;
        CanRefundToDebt = false;
        CanRefundServiceCharge = false;
        ServiceChargeRefundText = string.Empty;
        RefundServiceCharge = false;
        ReturnError = null;
        ReturnDoneText = string.Empty;
        OnPropertyChanged(nameof(HasReturnSelection));
    }
}
