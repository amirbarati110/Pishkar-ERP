using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ERP.Application.Sales;
using ERP.Domain.Common;

namespace ERP.Presentation.Features.Sales;

/// <summary>
/// «دیدن فاکتور»: a completed invoice opened by its number — from the product's کاردکس, where a
/// row names the invoice it came from. It shows the invoice as it stands now (a corrected
/// invoice opens as its correction) and offers the same four actions «فاکتورهای امروز» has:
/// اصلاح، سند، چاپ، مرجوعی. The actions are the existing commands, not copies — one rule each.
/// </summary>
public sealed partial class SalesWorkspaceViewModel
{
    public ObservableCollection<ReceiptLineRow> InvoiceViewLines { get; } = [];

    [ObservableProperty]
    public partial bool IsInvoiceViewOpen { get; set; }

    /// <summary>The same row type the invoice list uses, so اصلاح/سند/چاپ/مرجوعی take it unchanged.</summary>
    [ObservableProperty]
    public partial InvoiceListRow? ViewedInvoice { get; set; }

    [ObservableProperty]
    public partial string InvoiceViewTitleText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string InvoiceViewInfoText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string InvoiceViewSubtotalText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? InvoiceViewDiscountText { get; set; }

    [ObservableProperty]
    public partial string? InvoiceViewServiceChargeText { get; set; }

    [ObservableProperty]
    public partial string? InvoiceViewTaxText { get; set; }

    [ObservableProperty]
    public partial string InvoiceViewTotalText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? InvoiceViewNoteText { get; set; }

    [ObservableProperty]
    public partial string? InvoiceViewError { get; set; }

    /// <summary>Opens the invoice with this number. An unknown number, or one that is only a draft, shows a plain message instead.</summary>
    [RelayCommand]
    private Task OpenInvoiceByNumberAsync(long number) => RunAsync(async () =>
    {
        InvoiceViewError = null;
        InvoiceViewLines.Clear();
        ViewedInvoice = null;
        InvoiceViewTitleText = $"فاکتور {PersianNumber.DigitsToPersian(number.ToString(System.Globalization.CultureInfo.InvariantCulture))}";

        var found = await _backend.FindReturnInvoice.ExecuteAsync(new FindSaleForReturnQuery(number), CancellationToken.None);
        if (!found.IsSuccess)
        {
            InvoiceViewError = found.Error?.Message ?? "این فاکتور پیدا نشد.";
            IsInvoiceViewOpen = true;
            return;
        }

        var result = await _backend.Details.ExecuteAsync(new GetSaleDetailsQuery(found.Value), CancellationToken.None);
        if (!result.IsSuccess || result.Value is not { Totals: { } totals } details)
        {
            InvoiceViewError = result.Error?.Message ?? "این فاکتور هنوز تکمیل نشده است.";
            IsInvoiceViewOpen = true;
            return;
        }

        var item = new SaleListItem(
            details.SaleId,
            details.Number,
            details.OpenedAtUtc,
            details.CompletedAtUtc,
            details.CustomerId,
            details.CustomerName,
            details.Lines.Count,
            totals.Total,
            details.PaymentMethod);
        ViewedInvoice = new InvoiceListRow(item);

        InvoiceViewTitleText = $"فاکتور {ViewedInvoice.NumberText}";
        var when = details.CompletedAtUtc is { } completedAtUtc
            ? $"{PersianDate.FromUtc(completedAtUtc)} {SalesText.ClockTime(completedAtUtc)}"
            : string.Empty;
        var method = details.PaymentMethod is { } paymentMethod ? SalesText.PaymentMethodName(paymentMethod) : string.Empty;
        InvoiceViewInfoText = string.Join(
            " · ",
            new[] { ViewedInvoice.CustomerText, when, method }.Where(part => part.Length > 0));

        foreach (var line in details.Lines)
        {
            InvoiceViewLines.Add(new ReceiptLineRow(
                line.ProductName,
                $"{SalesText.Quantity(line.Quantity)} × {SalesText.Tomans(line.UnitPrice)}",
                SalesText.Tomans(line.LineTotal)));
        }

        InvoiceViewSubtotalText = SalesText.Tomans(totals.Subtotal);
        InvoiceViewDiscountText = totals.Discount.Rials > 0 ? SalesText.Tomans(totals.Discount) : null;
        InvoiceViewServiceChargeText = totals.ServiceCharge.Rials > 0 ? SalesText.Tomans(totals.ServiceCharge) : null;
        InvoiceViewTaxText = totals.Tax.Rials > 0 ? SalesText.Tomans(totals.Tax) : null;
        InvoiceViewTotalText = SalesText.Tomans(totals.Total);
        InvoiceViewNoteText = string.IsNullOrWhiteSpace(details.Note) ? null : details.Note;
        OnPropertyChanged(nameof(HasInvoiceViewDiscount));
        OnPropertyChanged(nameof(HasInvoiceViewServiceCharge));
        OnPropertyChanged(nameof(HasInvoiceViewTax));
        OnPropertyChanged(nameof(HasInvoiceViewNote));

        IsInvoiceViewOpen = true;
    });

    public bool HasInvoiceViewDiscount => !string.IsNullOrEmpty(InvoiceViewDiscountText);

    public bool HasInvoiceViewServiceCharge => !string.IsNullOrEmpty(InvoiceViewServiceChargeText);

    public bool HasInvoiceViewTax => !string.IsNullOrEmpty(InvoiceViewTaxText);

    public bool HasInvoiceViewNote => !string.IsNullOrEmpty(InvoiceViewNoteText);

    public bool HasInvoiceViewActions => ViewedInvoice is not null;

    partial void OnViewedInvoiceChanged(InvoiceListRow? value) => OnPropertyChanged(nameof(HasInvoiceViewActions));

    [RelayCommand]
    private void CloseInvoiceView() => IsInvoiceViewOpen = false;
}
