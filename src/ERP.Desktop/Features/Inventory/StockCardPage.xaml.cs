using ERP.Application.Catalog;
using ERP.Presentation.Features.Inventory;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;

namespace ERP.Desktop.Features.Inventory;

/// <summary>
/// کاردکس کالا. Opened with the product row chosen in «لیست کالاها». The page owns no rules:
/// <see cref="StockCardViewModel"/> reads the movements; this only wires the period chips
/// and Esc.
/// </summary>
public sealed partial class StockCardPage : Page
{
    private ProductListRow? _product;

    public StockCardPage()
    {
        ViewModel = new StockCardViewModel(App.Services.StockCard, App.Services.SalesBackend.Clock)
        {
            ReportUnexpectedError = App.TryLogCrash,
        };

        InitializeComponent();
        Loaded += OnLoaded;
        PreviewKeyDown += OnPagePreviewKeyDown;
    }

    public StockCardViewModel ViewModel { get; }

    /// <summary>A document that opens an invoice is drawn in the accent colour, so it reads as a link.</summary>
    public static Brush DocumentBrush(bool opensInvoice) =>
        (Brush)Microsoft.UI.Xaml.Application.Current.Resources[opensInvoice ? "SalesPrimaryDarkBrush" : "SalesMutedTextBrush"];

    public static Visibility WhenText(string? text) => string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;

    public static Brush BalanceBrush(bool isNegative) =>
        (Brush)Microsoft.UI.Xaml.Application.Current.Resources[isNegative ? "SalesAmberBrush" : "AppTextBrush"];

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnNavigatedTo(e);
        _product = e.Parameter as ProductListRow;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_product is not null)
        {
            await ViewModel.LoadAsync(_product, CancellationToken.None);
        }

        UpdateChips();
    }

    private async void OnPeriodClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && Enum.TryParse<StockCardPeriod>(tag, out var period))
        {
            await ViewModel.SelectPeriodCommand.ExecuteAsync(period);
            UpdateChips();
        }
    }

    private void UpdateChips()
    {
        foreach (var (button, period) in new[]
        {
            (AllChip, StockCardPeriod.All),
            (TodayChip, StockCardPeriod.Today),
            (WeekChip, StockCardPeriod.LastSevenDays),
            (MonthChip, StockCardPeriod.LastThirtyDays),
        })
        {
            var selected = ViewModel.SelectedPeriod == period;
            button.Background = Token(selected ? "AppPrimarySoftBrush" : "AppSurfaceBrush");
            button.BorderBrush = Token(selected ? "AppPrimaryBrush" : "SalesLineBrush");
            button.Foreground = Token(selected ? "SalesPrimaryDarkBrush" : "SalesMutedTextBrush");
        }
    }

    private static Brush Token(string key) => (Brush)Microsoft.UI.Xaml.Application.Current.Resources[key];

    private void OnRowClick(object sender, ItemClickEventArgs e)
    {
        // A sale opens its own invoice, a return opens the invoice it was taken against.
        if (e.ClickedItem is StockCardRow { RelatedSaleNumber: { } number })
        {
            MainPage.ShellOf(this)?.OpenSalesWorkspace(new ERP.Desktop.Features.Sales.OpenInvoiceRequest(number));
        }
    }

    private void OnBackClick(object sender, RoutedEventArgs e) => GoBack();

    private void GoBack()
    {
        if (Frame.CanGoBack)
        {
            Frame.GoBack();
        }
    }

    private void OnPagePreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            GoBack();
            e.Handled = true;
        }
    }
}
