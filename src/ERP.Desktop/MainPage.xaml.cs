using ERP.Desktop.Features.Categories;
using ERP.Desktop.Features.Inventory;
using ERP.Desktop.Features.Products;
using ERP.Desktop.Features.Sales;
using Microsoft.UI.Xaml.Controls;

namespace ERP.Desktop;

public sealed partial class MainPage : Page
{
    public MainPage()
    {
        InitializeComponent();
        ContentFrame.Navigate(typeof(DashboardPage));
    }

    /// <summary>
    /// Routes ContentFrame to the page matching the selected NavigationViewItem's
    /// Tag. NavigationView owns the selected-item visual state itself (via the
    /// ThemeResource aliases in MainPage.xaml), so no manual style/foreground
    /// bookkeeping is needed here — unlike the hand-rolled Button sidebar this
    /// replaced (source-of-truth Appendix A #10).
    /// </summary>
    private void OnNavigationSelectionChanged(
        NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem { Tag: string tag })
        {
            return;
        }

        // The sales workspace is its own full-window space with an exit button
        // (approved design): it replaces this page instead of opening inside it.
        if (tag == "sales")
        {
            sender.SelectedItem = HomeNavItem;
            Frame.Navigate(typeof(SalesPage));
            return;
        }

        var pageType = tag switch
        {
            "products" => typeof(ProductEditorPage),
            "categories" => typeof(CategoriesPage),
            "inventory" => typeof(OpeningStockPage),
            "settings" => typeof(SettingsPage),
            _ => typeof(DashboardPage),
        };

        if (ContentFrame.CurrentSourcePageType != pageType)
        {
            ContentFrame.Navigate(pageType);
        }
    }
}
