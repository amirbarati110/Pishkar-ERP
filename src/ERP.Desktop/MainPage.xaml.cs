using ERP.Desktop.Features.Categories;
using ERP.Desktop.Features.Inventory;
using ERP.Desktop.Features.Products;
using ERP.Desktop.Features.Sales;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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

    /// <summary>Opens the section with this Tag, exactly as clicking its menu item would.</summary>
    public void GoTo(string tag)
    {
        var item = RootNavigationView.MenuItems
            .Concat(RootNavigationView.FooterMenuItems)
            .OfType<NavigationViewItem>()
            .FirstOrDefault(candidate => (string?)candidate.Tag == tag);

        if (item is not null)
        {
            RootNavigationView.SelectedItem = item;
        }
    }

    /// <summary>
    /// The sales workspace is its own full-window space, so it replaces the
    /// shell rather than opening inside it. <paramref name="mode"/> is one of
    /// the <see cref="SalesPage"/> start constants.
    /// </summary>
    public void OpenSalesWorkspace(string mode) => Frame.Navigate(typeof(SalesPage), mode);

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
            OpenSalesWorkspace(SalesPage.StartFresh);
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
