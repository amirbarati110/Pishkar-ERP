using ERP.Desktop.Features.Backups;
using ERP.Desktop.Features.Cashiering;
using ERP.Desktop.Features.Categories;
using ERP.Desktop.Features.Customers;
using ERP.Desktop.Features.Importing;
using ERP.Desktop.Features.Inventory;
using ERP.Desktop.Features.Navigation;
using ERP.Desktop.Features.Products;
using ERP.Desktop.Features.Sales;
using ERP.Presentation.Features.Navigation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace ERP.Desktop;

public sealed partial class MainPage : Page
{
    public MainPage()
    {
        InitializeComponent();
        ContentFrame.Navigate(typeof(DashboardPage));
    }

    /// <summary>
    /// The shell a page inside <c>ContentFrame</c> is hosted by, or null if the
    /// page is not inside one. Pages ask for it instead of reaching up through
    /// Parent chains of their own, so the workbench can open a section and have
    /// the menu highlight follow (otherwise the pane keeps saying «میز کار»
    /// while another page is shown).
    /// </summary>
    public static MainPage? ShellOf(DependencyObject? child)
    {
        for (var node = child; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is MainPage shell)
            {
                return shell;
            }
        }

        return null;
    }

    /// <summary>
    /// Opens the screen behind a hub card (<see cref="NavigationMap"/> keys), exactly as
    /// clicking that card would: the menu highlight moves to the card's section and the
    /// breadcrumb names where the user is. The three sales cards open the full-window
    /// sales workspace instead of a page inside this shell.
    /// </summary>
    public void OpenCard(string cardKey)
    {
        switch (cardKey)
        {
            case NavigationMap.NewSale:
                OpenSalesWorkspace(SalesPage.StartFresh);
                return;
            case NavigationMap.HeldInvoices:
                OpenSalesWorkspace(SalesPage.StartOnInvoiceList);
                return;
            case NavigationMap.Returns:
                OpenSalesWorkspace(SalesPage.StartOnReturn);
                return;
        }

        var card = NavigationMap.FindCard(cardKey);
        var section = NavigationMap.SectionOf(cardKey);
        var pageType = PageOf(cardKey);
        if (card is null || section is null || pageType is null)
        {
            return;
        }

        SelectMenuItem(section.Key);
        ContentFrame.Navigate(pageType);
        BreadcrumbSection.Content = section.Title;
        BreadcrumbSection.Tag = section.Key;
        BreadcrumbPage.Text = card.Title;
        Breadcrumb.Visibility = Visibility.Visible;
        HelpOverlay.Workflow = null; // a new page means the old page's help no longer applies
    }

    /// <summary>
    /// The sales workspace is its own full-window space, so it replaces the
    /// shell rather than opening inside it. <paramref name="mode"/> is one of
    /// the <see cref="SalesPage"/> start constants.
    /// </summary>
    public void OpenSalesWorkspace(object mode) => Frame.Navigate(typeof(SalesPage), mode);

    private static Type? PageOf(string cardKey) => cardKey switch
    {
        NavigationMap.ProductList => typeof(ProductListPage),
        NavigationMap.CustomerList => typeof(CustomerListPage),
        NavigationMap.WarehouseList => typeof(WarehouseListPage),
        NavigationMap.Categories => typeof(CategoriesPage),
        NavigationMap.Import => typeof(ImportCenterPage),
        NavigationMap.CashShift => typeof(CashShiftPage),
        NavigationMap.OpeningStock => typeof(OpeningStockPage),
        NavigationMap.Backup => typeof(BackupPage),
        _ => null,
    };

    /// <summary>Highlights a menu item (by its Tag). Navigation is done on <c>ItemInvoked</c>, so moving the highlight from code never opens anything by itself.</summary>
    private void SelectMenuItem(string tag)
    {
        var item = RootNavigationView.MenuItems
            .Concat(RootNavigationView.FooterMenuItems)
            .OfType<NavigationViewItem>()
            .FirstOrDefault(candidate => (string?)candidate.Tag == tag);

        if (item is not null && !ReferenceEquals(RootNavigationView.SelectedItem, item))
        {
            RootNavigationView.SelectedItem = item;
        }
    }

    private void ShowSection(string sectionKey)
    {
        SelectMenuItem(sectionKey);
        ContentFrame.Navigate(typeof(HubPage), sectionKey);
        Breadcrumb.Visibility = Visibility.Collapsed;
        HelpOverlay.Workflow = null;
    }

    private void OnBreadcrumbSectionClick(object sender, RoutedEventArgs e)
    {
        if (BreadcrumbSection.Tag is string sectionKey)
        {
            ShowSection(sectionKey);
        }
    }

    /// <summary>
    /// A click (or Enter) on a menu item: «میز کار» opens the workbench, any other item
    /// opens its section's page of cards. <c>ItemInvoked</c> rather than
    /// <c>SelectionChanged</c> on purpose: while a card's screen is open its section is
    /// already the highlighted one, and clicking it must still bring the user back to the
    /// cards — a selection change would never fire. NavigationView owns the selected-item
    /// visual state itself (ThemeResource aliases in MainPage.xaml, source-of-truth
    /// Appendix A #10).
    /// </summary>
    private void OnNavigationItemInvoked(
        NavigationView sender,
        NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer?.Tag is not string tag)
        {
            return;
        }

        if (tag == NavigationMap.Home)
        {
            SelectMenuItem(tag);
            ContentFrame.Navigate(typeof(DashboardPage));
            Breadcrumb.Visibility = Visibility.Collapsed;
            HelpOverlay.Workflow = null;
            return;
        }

        ShowSection(tag);
    }

    // ───── راهنمای این صفحه (§4.1/§3.14) ─────

    private void OnHelpKeyInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        ToggleHelp();
    }

    private void OnHelpButtonClick(object sender, RoutedEventArgs e) => ToggleHelp();

    private void OnHelpCloseRequested(object sender, EventArgs e) => HelpOverlay.Workflow = null;

    private void ToggleHelp()
    {
        if (HelpOverlay.Workflow is not null)
        {
            HelpOverlay.Workflow = null;
            return;
        }

        var page = ContentFrame.CurrentSourcePageType switch
        {
            var type when type == typeof(CategoriesPage) => "categories",
            var type when type == typeof(ProductListPage) => "product-list",
            var type when type == typeof(CustomerListPage) => "customer-list",
            var type when type == typeof(WarehouseListPage) => "warehouse-list",
            var type when type == typeof(Features.Inventory.StockCardPage) => "stock-card",
            var type when type == typeof(ProductEditorPage) => "product-editor",
            var type when type == typeof(OpeningStockPage) => "opening-stock",
            var type when type == typeof(ImportCenterPage) => "import-center",
            var type when type == typeof(CashShiftPage) => "cash-shift",
            var type when type == typeof(BackupPage) => "backup",
            var type when type == typeof(HubPage) => "hub",
            _ => "home",
        };

        HelpOverlay.Workflow = App.Workflows.Load(page);
    }
}
