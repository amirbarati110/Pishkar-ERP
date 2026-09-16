using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ERP.Application.Common;
using ERP.Application.Sales;
using ERP.Domain.Common;
using ERP.Presentation.Features.Sales;

namespace ERP.Presentation.Features.Workbench;

/// <summary>One invoice as the workbench lists it — number/customer on one line, amount on the other.</summary>
public sealed record WorkbenchInvoiceRow(SaleListItem Item)
{
    public string Title => Item.Number is { } number ? $"فاکتور {number.ToPersianString()}" : "پیش‌نویس";

    public string Customer => string.IsNullOrWhiteSpace(Item.CustomerName) ? "مشتری نقدی" : Item.CustomerName!;

    public string ItemCountText => $"{PersianNumber.FormatGrouped(Item.ItemCount)} قلم";

    public string CustomerAndCount => $"{Customer} · {ItemCountText}";

    public string AmountText => Item.Amount is { } amount ? $"{SalesText.Tomans(amount)} تومان" : "—";

    public string TimeText => SalesText.ClockTime(Item.CompletedAtUtc ?? Item.OpenedAtUtc);
}

/// <summary>
/// «میز کار» for the cashier (source-of-truth §3.13): only what is frequent and
/// actionable — today's till figures, the invoices left hanging, and the way
/// back into a sale. Deliberately not a manager's dashboard of charts; §3.13
/// says the workbench «حق ندارد به صفحه‌ای شلوغ با ده‌ها نمودار تبدیل شود».
///
/// Every number here is read from the database. Things the program cannot
/// answer yet (shift totals, returns) are not faked: the page marks them as
/// not built, per the user's honesty rule.
/// </summary>
public partial class WorkbenchViewModel : ObservableObject
{
    private readonly IListSalesOfDayHandler _salesOfDay;
    private readonly IListHeldSalesHandler _heldSales;
    private readonly IClock _clock;

    public WorkbenchViewModel(IListSalesOfDayHandler salesOfDay, IListHeldSalesHandler heldSales, IClock clock)
    {
        _salesOfDay = salesOfDay;
        _heldSales = heldSales;
        _clock = clock;
    }

    public ObservableCollection<WorkbenchInvoiceRow> RecentInvoices { get; } = [];

    public ObservableCollection<WorkbenchInvoiceRow> HeldInvoices { get; } = [];

    [ObservableProperty]
    public partial string DateText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TodayTotalText { get; set; } = "—";

    [ObservableProperty]
    public partial string TodayCountText { get; set; } = "—";

    [ObservableProperty]
    public partial string HeldCountText { get; set; } = "—";

    [ObservableProperty]
    public partial string PaymentBreakdownText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string? LoadError { get; set; }

    public bool HasNoRecentInvoices => RecentInvoices.Count == 0;

    public bool HasNoHeldInvoices => HeldInvoices.Count == 0;

    public bool HasPaymentBreakdown => PaymentBreakdownText.Length > 0;

    public bool HasLoadError => LoadError is not null;

    /// <summary>What the «فاکتورهای معلق» tile says under its title.</summary>
    public string HeldSummaryText => HeldInvoices.Count == 0
        ? "چیزی معلق نمانده است"
        : $"{PersianNumber.FormatGrouped(HeldInvoices.Count)} فاکتور در انتظار ادامه";

    /// <summary>How many rows of each list fit without turning the page into a report.</summary>
    private const int MaxRows = 6;

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        LoadError = null;

        try
        {
            var today = PersianDate.FromUtc(_clock.UtcNow);
            DateText = SalesText.LongDate(today);

            var day = await _salesOfDay
                .ExecuteAsync(new ListSalesOfDayQuery(today), cancellationToken)
                .ConfigureAwait(true);
            var held = await _heldSales
                .ExecuteAsync(new ListHeldSalesQuery(), cancellationToken)
                .ConfigureAwait(true);

            TodayTotalText = SalesText.Tomans(day.Total);
            TodayCountText = PersianNumber.FormatGrouped(day.Sales.Count);
            HeldCountText = PersianNumber.FormatGrouped(held.Count);
            PaymentBreakdownText = string.Join(
                " · ",
                day.TotalsByPaymentMethod
                    .OrderBy(pair => pair.Key)
                    .Select(pair => $"{SalesText.PaymentMethodName(pair.Key)} {SalesText.Tomans(pair.Value)}"));

            if (day.InvoicesWithoutTotal > 0)
            {
                // Invoices completed before totals were stored: say so rather
                // than let the day's sum look wrong for no reason.
                PaymentBreakdownText = string.Join(
                    " · ",
                    new[] { PaymentBreakdownText, $"{PersianNumber.FormatGrouped(day.InvoicesWithoutTotal)} فاکتور بدون مبلغ ذخیره‌شده" }
                        .Where(part => part.Length > 0));
            }

            Fill(RecentInvoices, day.Sales);
            Fill(HeldInvoices, held);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // §15.20: the cashier is told in plain words, never shown a stack
            // trace. The caller logs the real exception.
            LoadError = "خواندن فروش امروز و فاکتورهای معلق ناموفق بود. دکمه‌ی «به‌روزرسانی» را بزنید.";
            ReportUnexpectedError?.Invoke(exception);
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(HasNoRecentInvoices));
            OnPropertyChanged(nameof(HasNoHeldInvoices));
            OnPropertyChanged(nameof(HasPaymentBreakdown));
            OnPropertyChanged(nameof(HasLoadError));
            OnPropertyChanged(nameof(HeldSummaryText));
        }
    }

    /// <summary>Set by the page so a failure reaches the crash log, not just the screen.</summary>
    public Action<Exception>? ReportUnexpectedError { get; set; }

    private static void Fill(ObservableCollection<WorkbenchInvoiceRow> target, IReadOnlyList<SaleListItem> items)
    {
        target.Clear();
        foreach (var item in items.Take(MaxRows))
        {
            target.Add(new WorkbenchInvoiceRow(item));
        }
    }
}
