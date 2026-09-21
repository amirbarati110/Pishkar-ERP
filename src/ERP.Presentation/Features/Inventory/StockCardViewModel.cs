using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ERP.Application.Catalog;
using ERP.Application.Common;
using ERP.Application.Inventory;
using ERP.Domain.Common;
using ERP.Domain.Inventory;
using ERP.Presentation.Features.Sales;

namespace ERP.Presentation.Features.Inventory;

/// <summary>How far back the card looks. Chips, not date pickers — one touch, no typing.</summary>
public enum StockCardPeriod
{
    All = 0,
    Today = 1,
    LastSevenDays = 2,
    LastThirtyDays = 3,
}

/// <summary>One line of the card as the screen writes it.</summary>
public sealed class StockCardRow
{
    public StockCardRow(StockCardEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        DateText = $"{PersianDate.FromUtc(entry.OccurredAtUtc)}  {SalesText.ClockTime(entry.OccurredAtUtc)}";
        TypeText = StockCardText.TypeName(entry.Type);
        DocumentText = StockCardText.Document(entry.DocumentKind, entry.DocumentNumber);
        InText = entry.In == 0 ? string.Empty : SalesText.Quantity(entry.In);
        OutText = entry.Out == 0 ? string.Empty : SalesText.Quantity(entry.Out);
        BalanceText = SalesText.Quantity(entry.Balance);
        var quantity = entry.In > 0 ? entry.In : entry.Out;
        UnitCostText = entry.Value is { } value && quantity > 0
            ? SalesText.Tomans((long)Math.Round(value.Rials / quantity, MidpointRounding.AwayFromZero))
            : StockCardText.Unknown;
        ValueText = entry.Value is { } total ? SalesText.Tomans(total) : StockCardText.Unknown;
        BalanceValueText = entry.BalanceValue is { } running ? SalesText.Tomans(running) : StockCardText.Unknown;
        IsNegativeBalance = entry.Balance < 0;
        RelatedSaleNumber = entry.RelatedSaleNumber;
        AccessibleName =
            $"{DateText}، {TypeText}{(DocumentText.Length > 0 ? "، " + DocumentText : string.Empty)}، " +
            $"{(entry.In > 0 ? "ورودی " + InText : "خروجی " + OutText)}، مانده {BalanceText}" +
            (entry.RelatedSaleNumber is not null ? "؛ برای دیدن فاکتور Enter بزنید" : string.Empty);
    }

    public string DateText { get; }

    public string TypeText { get; }

    public string DocumentText { get; }

    public string InText { get; }

    public string OutText { get; }

    public string BalanceText { get; }

    /// <summary>«فی» — the cost price of one unit in this movement (بهای تمام‌شده, FIFO), in Tomans; «—» when never recorded.</summary>
    public string UnitCostText { get; }

    /// <summary>«مبلغ» — quantity × فی, in Tomans; «—» when never recorded.</summary>
    public string ValueText { get; }

    /// <summary>«مبلغ مانده» — what the stock is worth (at cost) after this movement, in Tomans; «—» when an earlier movement has no recorded cost.</summary>
    public string BalanceValueText { get; }

    /// <summary>A balance below zero means goods were sold ahead of being received — drawn in the warning colour.</summary>
    public bool IsNegativeBalance { get; }

    public string AccessibleName { get; }

    /// <summary>The invoice a click opens (the original invoice for a return); null when the row has none.</summary>
    public long? RelatedSaleNumber { get; }

    public bool CanOpenInvoice => RelatedSaleNumber is not null;
}

/// <summary>The words of the card, in one place.</summary>
public static class StockCardText
{
    /// <summary>What a cost column shows when the cost was never recorded — not zero, not a guess.</summary>
    public const string Unknown = "—";

    public static string TypeName(StockMovementType type) => type switch
    {
        StockMovementType.OpeningBalance => "موجودی اول دوره",
        StockMovementType.Receipt => "رسید کالا",
        StockMovementType.Sale => "فروش",
        StockMovementType.Return => "مرجوعی",
        StockMovementType.AdjustmentIncrease => "اصلاح موجودی (افزایش)",
        StockMovementType.AdjustmentDecrease => "اصلاح موجودی (کاهش)",
        StockMovementType.BackorderSale => "فروش بیش از موجودی",
        _ => "حرکت انبار",
    };

    public static string Document(StockCardDocumentKind kind, long? number) => (kind, number) switch
    {
        (StockCardDocumentKind.Sale, { } value) => $"فاکتور {PersianNumber.DigitsToPersian(value.ToString(System.Globalization.CultureInfo.InvariantCulture))}",
        (StockCardDocumentKind.Return, { } value) => $"مرجوعی {PersianNumber.DigitsToPersian(value.ToString(System.Globalization.CultureInfo.InvariantCulture))}",
        _ => string.Empty,
    };
}

/// <summary>
/// «کاردکس کالا»: opened from «لیست کالاها» (F7) for the selected product. It only reads —
/// nothing here changes stock.
/// </summary>
public sealed partial class StockCardViewModel : ObservableObject
{
    private readonly IGetStockCardHandler _handler;
    private readonly IClock _clock;
    private ProductListRow? _product;

    public StockCardViewModel(IGetStockCardHandler handler, IClock clock)
    {
        _handler = handler;
        _clock = clock;
    }

    /// <summary>Where an unexpected failure is written (the app's log). Null only in tests, where it should surface.</summary>
    public Action<Exception>? ReportUnexpectedError { get; set; }

    public ObservableCollection<StockCardRow> Rows { get; } = [];

    [ObservableProperty]
    public partial string ProductName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CodeLine { get; set; } = string.Empty;

    [ObservableProperty]
    public partial StockCardPeriod SelectedPeriod { get; set; } = StockCardPeriod.All;

    [ObservableProperty]
    public partial string OpeningText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TotalInText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TotalOutText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ClosingText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string OpeningValueText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TotalInValueText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TotalOutValueText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ClosingValueText { get; set; } = string.Empty;

    /// <summary>Filled when some movements have no recorded cost — said plainly, not hidden.</summary>
    [ObservableProperty]
    public partial string UnvaluedNoticeText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    public async Task LoadAsync(ProductListRow product, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(product);

        _product = product;
        ProductName = product.Name;
        await RefreshAsync(cancellationToken);
    }

    [RelayCommand]
    private async Task SelectPeriodAsync(StockCardPeriod period)
    {
        SelectedPeriod = period;
        await RefreshAsync(CancellationToken.None);
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (_product is null)
        {
            return;
        }

        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var today = PersianDate.FromUtc(_clock.UtcNow);
            DateTimeOffset? from = SelectedPeriod switch
            {
                StockCardPeriod.Today => today.StartUtc,
                StockCardPeriod.LastSevenDays => today.AddDays(-6).StartUtc,
                StockCardPeriod.LastThirtyDays => today.AddDays(-29).StartUtc,
                _ => null,
            };

            var page = await _handler.ExecuteAsync(new StockCardQuery(_product.Id, from), cancellationToken);
            if (page is null)
            {
                ErrorMessage = "این کالا پیدا نشد؛ ممکن است حذف شده باشد.";
                return;
            }

            ProductName = page.ProductName;
            CodeLine = page.Sku is { } sku ? $"کد {PersianNumber.DigitsToPersian(sku)} · واحد {page.UnitSymbol}" : $"واحد {page.UnitSymbol}";
            Rows.Clear();
            foreach (var entry in page.Entries)
            {
                Rows.Add(new StockCardRow(entry));
            }

            IsEmpty = Rows.Count == 0;
            OpeningText = SalesText.Quantity(page.OpeningBalance);
            TotalInText = SalesText.Quantity(page.TotalIn);
            TotalOutText = SalesText.Quantity(page.TotalOut);
            ClosingText = SalesText.Quantity(page.ClosingBalance);
            OpeningValueText = page.OpeningValue is { } opening ? SalesText.Tomans(opening) : StockCardText.Unknown;
            TotalInValueText = SalesText.Tomans(page.TotalInValue);
            TotalOutValueText = SalesText.Tomans(page.TotalOutValue);
            ClosingValueText = page.ClosingValue is { } closing ? SalesText.Tomans(closing) : StockCardText.Unknown;
            UnvaluedNoticeText = page.UnvaluedMovements > 0
                ? $"بهای {PersianNumber.FormatGrouped(page.UnvaluedMovements)} حرکت ثبت نشده (فروش قدیمی یا بیش از موجودی)؛ «مبلغ مانده» از آن‌جا نامعلوم است."
                : string.Empty;
        }
        catch (Exception exception) when (ReportUnexpectedError is not null)
        {
            ReportUnexpectedError(exception);
            ErrorMessage = "کاردکس خوانده نشد و خطا برای پشتیبانی ثبت شد. دوباره امتحان کنید.";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
