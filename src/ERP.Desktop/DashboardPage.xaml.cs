using ERP.Desktop.Features.Sales;
using ERP.Presentation.Features.Workbench;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ERP.Desktop;

/// <summary>
/// میز کار. The page owns no rules: it reads today's till figures and the held
/// invoices through <see cref="WorkbenchViewModel"/> and turns each tile into a
/// navigation. «فروش جدید» and «فاکتورهای معلق» both open the sales workspace —
/// the second one with its invoice list already open, so continuing a held
/// invoice is two clicks from here.
/// </summary>
public sealed partial class DashboardPage : Page
{
    public DashboardPage()
    {
        ViewModel = new WorkbenchViewModel(
            App.Services.SalesBackend.SalesOfDay,
            App.Services.SalesBackend.HeldSales,
            App.Services.Cashiering,
            App.Services.Defaults.MainWarehouseId,
            App.Services.SalesBackend.Clock)
        {
            ReportUnexpectedError = App.TryLogCrash,
        };

        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.LoadAsync(CancellationToken.None);
    }

    public WorkbenchViewModel ViewModel { get; }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) =>
        await ViewModel.LoadAsync(CancellationToken.None);

    private void OnNewSaleClick(object sender, RoutedEventArgs e) => OpenSales(SalesPage.StartFresh);

    private void OnHeldInvoicesClick(object sender, RoutedEventArgs e) => OpenSales(SalesPage.StartOnInvoiceList);

    private void OpenSales(string mode) => MainPage.ShellOf(this)?.OpenSalesWorkspace(mode);

    // Through the shell, so the menu highlight moves with the page.
    private void OnCategoriesClick(object sender, RoutedEventArgs e) => MainPage.ShellOf(this)?.GoTo("categories");

    private void OnProductsClick(object sender, RoutedEventArgs e) => MainPage.ShellOf(this)?.GoTo("products");

    private void OnInventoryClick(object sender, RoutedEventArgs e) => MainPage.ShellOf(this)?.GoTo("inventory");

    private void OnTillClick(object sender, RoutedEventArgs e) => MainPage.ShellOf(this)?.GoTo("till");
}
