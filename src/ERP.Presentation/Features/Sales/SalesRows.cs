using CommunityToolkit.Mvvm.ComponentModel;
using ERP.Application.Customers;
using ERP.Application.Sales;
using ERP.Domain.Catalog;
using ERP.Domain.Common;
using ERP.Domain.Customers;
using ERP.Domain.Inventory;
using ERP.Domain.Sales;

namespace ERP.Presentation.Features.Sales;

public enum StockLevel
{
    Normal = 1,
    Low = 2,
    Out = 3,
}

/// <summary>One row of the invoice table.</summary>
public sealed class CartLineRow
{
    public CartLineRow(SaleLineDetails line)
    {
        ArgumentNullException.ThrowIfNull(line);

        Line = line;
        ProductId = line.ProductId;
        Name = line.ProductName;
        CodeText = line.Sku is { } sku ? $"کد {PersianNumber.DigitsToPersian(sku)}" : string.Empty;
        UnitSymbol = line.UnitSymbol;
        QuantityText = SalesText.Quantity(line.Quantity);
        UnitPriceText = SalesText.Tomans(line.UnitPrice);
        DiscountText = line.Discount.Rials == 0 ? string.Empty : SalesText.Tomans(line.Discount);
        LineTotalText = SalesText.Tomans(line.LineTotal);

        // §6.9/§6.11: the cashier must see when this row's price is not the
        // catalogue price, and when the row asks for more than is on hand.
        var notes = new List<string>();
        if (line.PriceOverridden)
        {
            notes.Add("قیمت این فاکتور تغییر کرده");
        }

        if (line.Quantity > line.Available.Value)
        {
            notes.Add($"بیش از موجودی ({SalesText.Quantity(line.Available.Value)})");
        }

        WarningText = string.Join(" · ", notes);
    }

    public SaleLineDetails Line { get; }

    public ProductId ProductId { get; }

    public string Name { get; }

    public string CodeText { get; }

    public string UnitSymbol { get; }

    public string QuantityText { get; }

    public string UnitPriceText { get; }

    public string DiscountText { get; }

    public string LineTotalText { get; }

    public string WarningText { get; }

    public bool HasWarning => WarningText.Length > 0;
}

/// <summary>One row of the middle product list, or one tile.</summary>
public sealed class ProductRow
{
    public ProductRow(ProductId id, string name, string? sku, string unitSymbol, Money price, StockBalance? available)
    {
        Id = id;
        Name = name;
        CodeText = sku is null ? string.Empty : $"کد {PersianNumber.DigitsToPersian(sku)}";
        UnitSymbol = unitSymbol;
        PriceText = SalesText.Tomans(price);
        AvailableText = available is { } stock ? SalesText.Quantity(stock.Value) : "—";
        StockLevel = available is not { } balance
            ? StockLevel.Normal
            : balance.Value <= 0
                ? StockLevel.Out
                : balance.Value <= BrowseProductsForSaleHandler.LowStockThreshold ? StockLevel.Low : StockLevel.Normal;
    }

    public ProductId Id { get; }

    public string Name { get; }

    public string CodeText { get; }

    public string UnitSymbol { get; }

    public string PriceText { get; }

    public string AvailableText { get; }

    public StockLevel StockLevel { get; }
}

/// <summary>A category chip; <see cref="Id"/> null is «همه».</summary>
public sealed partial class CategoryChip : ObservableObject
{
    public CategoryChip(CategoryId? id, string name)
    {
        Id = id;
        Name = name;
    }

    public CategoryId? Id { get; }

    public string Name { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

/// <summary>A row of «لیست فاکتورها».</summary>
public sealed class InvoiceListRow
{
    public InvoiceListRow(SaleListItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        Item = item;
        NumberText = item.Number?.ToPersianString() ?? "پیش‌نویس";
        CustomerText = item.CustomerName ?? "مشتری نقدی";
        TimeText = PersianNumber.DigitsToPersian(
            PersianDate.TimeOfDayInIran(item.CompletedAtUtc ?? item.OpenedAtUtc).ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture));
        ItemCountText = PersianNumber.FormatGrouped(item.ItemCount);
        AmountText = item.Amount is { } amount ? SalesText.Tomans(amount) : "—";
        MethodText = item.PaymentMethod is { } method ? SalesText.PaymentMethodName(method) : string.Empty;
    }

    public SaleListItem Item { get; }

    public string NumberText { get; }

    public string CustomerText { get; }

    public string TimeText { get; }

    public string ItemCountText { get; }

    public string AmountText { get; }

    public string MethodText { get; }
}

/// <summary>
/// One open invoice — a tab on the sales screen. Holds only what the server
/// returned last time (<see cref="Apply"/>) plus what the cashier is typing
/// into the footer; it never computes money itself.
/// </summary>
public sealed partial class InvoiceTab : ObservableObject
{
    /// <summary>
    /// Iran's general VAT rate as of 1404 is 10% (up from 9%) — سپیدار
    /// (https://www.sepidarsystem.com/blog/vat-rate/) and فرارو
    /// (https://fararu.com/fa/news/981799/). Still just a starting point the
    /// cashier can edit per invoice, not a rule enforced here: exempt or
    /// reduced-rate goods (١٪ for staples, 25–40% for tobacco) need a rate per
    /// product, tracked as checklist ج — not built yet.
    /// </summary>
    public const decimal DefaultTaxRatePercent = 10m;

    public InvoiceTab(SaleId saleId)
    {
        SaleId = saleId;
    }

    public SaleId SaleId { get; private set; }

    public System.Collections.ObjectModel.ObservableCollection<CartLineRow> Lines { get; } = [];

    [ObservableProperty]
    public partial string Title { get; set; } = "مشتری نقدی";

    [ObservableProperty]
    public partial CustomerId? CustomerId { get; set; }

    /// <summary>
    /// True once the server confirms this draft is a «صورتحساب اصلاحی»
    /// (§10.10) — set from <see cref="SaleDetails.CorrectsSaleId"/> every
    /// <see cref="Apply"/>, so it is still correct after a reload even though
    /// <see cref="CorrectionOfNumberText"/> (a client-side convenience, not
    /// re-derived from the server) is not.
    /// </summary>
    [ObservableProperty]
    public partial bool IsCorrection { get; set; }

    /// <summary>
    /// «۱۲۵۸» — set once, when the correction is opened from its row on
    /// «فاکتورهای امروز» (<see cref="SalesWorkspaceViewModel"/>'s
    /// OpenCorrectionAsync). Best-effort only: reopening a held correction in
    /// a later session leaves this blank — <see cref="IsCorrection"/> alone
    /// still protects the invoice; only the friendly «کدام فاکتور» reminder
    /// is unavailable without another round trip this version does not make.
    /// </summary>
    [ObservableProperty]
    public partial string CorrectionOfNumberText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CustomerName { get; set; } = "مشتری نقدی";

    [ObservableProperty]
    public partial string CustomerMobileText { get; set; } = string.Empty;

    /// <summary>«مانده قبلی: ۱,۸۵۰,۰۰۰ تومان بدهکار · ۲ فاکتور باز» — empty when there is nothing to warn about.</summary>
    [ObservableProperty]
    public partial string BalanceText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SubtotalText { get; set; } = "۰";

    /// <summary>«توضیحات فاکتور» — free text, saved on the invoice itself (approved design).</summary>
    [ObservableProperty]
    public partial string NoteInput { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DiscountInput { get; set; } = "۰";

    [ObservableProperty]
    public partial string DiscountPercentInput { get; set; } = "۰";

    [ObservableProperty]
    public partial string ServiceChargeInput { get; set; } = "۰";

    [ObservableProperty]
    public partial string TaxPercentInput { get; set; } = SalesText.Percent(DefaultTaxRatePercent);

    [ObservableProperty]
    public partial string TaxText { get; set; } = "۰";

    [ObservableProperty]
    public partial string TotalText { get; set; } = "۰";

    [ObservableProperty]
    public partial bool IsEmpty { get; set; } = true;

    /// <summary>True for the tab whose invoice is on screen — drives the tab's highlighted look.</summary>
    [ObservableProperty]
    public partial bool IsCurrent { get; set; }

    public decimal TaxRatePercent { get; set; } = DefaultTaxRatePercent;

    public Money Subtotal { get; private set; } = Money.Zero;

    public Money Payable { get; private set; } = Money.Zero;

    public int ItemCount => Lines.Count;

    public bool HasBalanceWarning => BalanceText.Length > 0;

    partial void OnBalanceTextChanged(string value) => OnPropertyChanged(nameof(HasBalanceWarning));

    /// <summary>Starts over on a new draft in the same tab — after completion or removal.</summary>
    public void Reset(SaleId saleId)
    {
        SaleId = saleId;
        TaxRatePercent = DefaultTaxRatePercent;
        TaxPercentInput = SalesText.Percent(DefaultTaxRatePercent);
        IsCorrection = false;
        CorrectionOfNumberText = string.Empty;
        CustomerId = null;
        CustomerName = "مشتری نقدی";
        CustomerMobileText = string.Empty;
        BalanceText = string.Empty;
        Lines.Clear();
        Subtotal = Money.Zero;
        Payable = Money.Zero;
        SubtotalText = TaxText = TotalText = "۰";
        NoteInput = string.Empty;
        DiscountInput = DiscountPercentInput = ServiceChargeInput = "۰";
        IsEmpty = true;
        UpdateTitle();
    }

    public void Apply(SaleDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);

        Lines.Clear();
        foreach (var line in details.Lines)
        {
            Lines.Add(new CartLineRow(line));
        }

        CustomerId = details.CustomerId;
        CustomerName = details.CustomerName ?? "مشتری نقدی";
        IsCorrection = details.CorrectsSaleId is not null;
        if (details.CustomerId is null)
        {
            CustomerMobileText = string.Empty;
            BalanceText = string.Empty;
        }

        Subtotal = details.Subtotal;
        SubtotalText = SalesText.Tomans(details.Subtotal);
        NoteInput = details.Note ?? string.Empty;
        DiscountInput = SalesText.Tomans(details.Discount);
        DiscountPercentInput = details.Subtotal.Rials == 0
            ? "۰"
            : SalesText.Percent(details.Discount.Rials * 100m / details.Subtotal.Rials);
        ServiceChargeInput = SalesText.Tomans(details.ServiceCharge);

        var footer = details.Totals ?? details.Preview;
        Payable = footer?.Total ?? details.Subtotal;
        TaxText = footer is null ? "۰" : SalesText.Tomans(footer.Tax);
        TotalText = SalesText.Tomans(Payable);
        IsEmpty = Lines.Count == 0;
        UpdateTitle();
    }

    public void ApplyAccount(CustomerAccountSummary account)
    {
        ArgumentNullException.ThrowIfNull(account);

        CustomerMobileText = PersianNumber.DigitsToPersian(account.Mobile);
        var parts = new List<string>();
        if (account.Debt.Rials > 0)
        {
            parts.Add($"مانده قبلی: {SalesText.Tomans(account.Debt)} تومان بدهکار");
        }

        if (account.OpenInvoiceNumbers.Count > 0)
        {
            parts.Add($"{PersianNumber.FormatGrouped(account.OpenInvoiceNumbers.Count)} فاکتور باز");
        }

        if (account.Advance.Rials > 0)
        {
            parts.Add($"{SalesText.Tomans(account.Advance)} تومان بستانکار");
        }

        BalanceText = string.Join(" · ", parts);
    }

    private void UpdateTitle()
    {
        Title = IsCorrection
            ? $"اصلاحیه‌ی {(CorrectionOfNumberText.Length > 0 ? CorrectionOfNumberText : "؟")}"
            : Lines.Count == 0
                ? CustomerName
                : $"{CustomerName} · {PersianNumber.FormatGrouped(Lines.Count)}";
        OnPropertyChanged(nameof(ItemCount));
    }
}
