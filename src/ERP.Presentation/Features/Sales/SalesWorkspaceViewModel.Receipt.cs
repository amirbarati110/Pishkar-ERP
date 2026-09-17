using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ERP.Application.Sales;
using ERP.Domain.Common;
using ERP.Domain.Sales;

namespace ERP.Presentation.Features.Sales;

/// <summary>One line of the on-screen receipt.</summary>
public sealed record ReceiptLineRow(string Name, string QuantityAndPrice, string TotalText);

/// <summary>
/// «پیش‌نمایش و چاپ رسید پایه» (milestone-1 §14.14/§15 acceptance criterion 8).
/// Always rebuilt fresh from <see cref="SaleDetails"/> — reprinting never
/// touches the sale, payment or stock, so "چاپ مجدد" is just asking to look
/// at the same already-completed invoice again (§16 «قطع چاپ باعث تکرار ثبت
/// فروش نمی‌شود»).
/// </summary>
public sealed partial class SalesWorkspaceViewModel
{
    public ObservableCollection<ReceiptLineRow> ReceiptLines { get; } = [];

    [ObservableProperty]
    public partial bool IsReceiptPreviewOpen { get; set; }

    [ObservableProperty]
    public partial string ReceiptNumberText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ReceiptDateText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ReceiptCustomerText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ReceiptSubtotalText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? ReceiptDiscountText { get; set; }

    [ObservableProperty]
    public partial string? ReceiptServiceChargeText { get; set; }

    [ObservableProperty]
    public partial string? ReceiptTaxText { get; set; }

    [ObservableProperty]
    public partial string ReceiptTotalText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ReceiptPaymentMethodText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? ReceiptNoteText { get; set; }

    [ObservableProperty]
    public partial string? ReceiptError { get; set; }

    public bool HasReceiptDiscount => !string.IsNullOrEmpty(ReceiptDiscountText);

    public bool HasReceiptServiceCharge => !string.IsNullOrEmpty(ReceiptServiceChargeText);

    public bool HasReceiptTax => !string.IsNullOrEmpty(ReceiptTaxText);

    public bool HasReceiptNote => !string.IsNullOrEmpty(ReceiptNoteText);

    /// <summary>«چاپ رسید» in the completion summary, or «چاپ» on a row of «فاکتورهای امروز» — same one path either way.</summary>
    [RelayCommand]
    private Task OpenReceiptPreviewAsync(SaleId? saleId) => RunAsync(async () =>
    {
        if (saleId is not { } id)
        {
            return;
        }

        ReceiptError = null;
        ReceiptLines.Clear();

        var result = await _backend.Details.ExecuteAsync(new GetSaleDetailsQuery(id), CancellationToken.None);
        if (!result.IsSuccess || result.Value is not { Totals: { } totals } details)
        {
            ReceiptError = result.Error?.Message ?? "این فاکتور هنوز تکمیل نشده است.";
            IsReceiptPreviewOpen = true;
            return;
        }

        ReceiptNumberText = details.Number?.ToPersianString() ?? string.Empty;
        ReceiptDateText = details.CompletedAtUtc is { } completedAtUtc
            ? $"{SalesText.LongDate(PersianDate.FromUtc(completedAtUtc))} {SalesText.ClockTime(completedAtUtc)}"
            : string.Empty;
        ReceiptCustomerText = string.IsNullOrWhiteSpace(details.CustomerName) ? "مشتری نقدی" : details.CustomerName!;

        foreach (var line in details.Lines)
        {
            ReceiptLines.Add(new ReceiptLineRow(
                line.ProductName,
                $"{SalesText.Quantity(line.Quantity)} × {SalesText.Tomans(line.UnitPrice)}",
                SalesText.Tomans(line.LineTotal)));
        }

        ReceiptSubtotalText = SalesText.Tomans(totals.Subtotal);
        ReceiptDiscountText = totals.Discount.Rials > 0 ? SalesText.Tomans(totals.Discount) : null;
        ReceiptServiceChargeText = totals.ServiceCharge.Rials > 0 ? SalesText.Tomans(totals.ServiceCharge) : null;
        ReceiptTaxText = totals.Tax.Rials > 0 ? SalesText.Tomans(totals.Tax) : null;
        ReceiptTotalText = SalesText.Tomans(totals.Total);
        ReceiptPaymentMethodText = details.PaymentMethod is { } method ? SalesText.PaymentMethodName(method) : string.Empty;
        ReceiptNoteText = string.IsNullOrWhiteSpace(details.Note) ? null : details.Note;

        OnPropertyChanged(nameof(HasReceiptDiscount));
        OnPropertyChanged(nameof(HasReceiptServiceCharge));
        OnPropertyChanged(nameof(HasReceiptTax));
        OnPropertyChanged(nameof(HasReceiptNote));

        IsReceiptPreviewOpen = true;
    });

    [RelayCommand]
    private void CloseReceiptPreview() => IsReceiptPreviewOpen = false;
}
