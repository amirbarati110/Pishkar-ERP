using ERP.Presentation.Features.Products;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace ERP.Desktop.Features.Products;

/// <summary>
/// لیست کالاها. The page owns no rules: <see cref="ProductListViewModel"/> reads, filters and
/// archives; this only turns the bottom bar's keys (F2/F3/F4/F7/Esc) and buttons into
/// «open the product form» or «go back».
/// </summary>
public sealed partial class ProductListPage : Page
{
    public ProductListPage()
    {
        ViewModel = new ProductListViewModel(
            App.Services.ProductList,
            App.Services.RetailSetup,
            App.Services.CatalogLookup)
        {
            ReportUnexpectedError = App.TryLogCrash,
        };

        InitializeComponent();
        Loaded += OnLoaded;
        PreviewKeyDown += OnPagePreviewKeyDown;
    }

    public ProductListViewModel ViewModel { get; }

    // ───── x:Bind helpers ─────

    public static Brush StockBrush(bool isOutOfStock) =>
        (Brush)Microsoft.UI.Xaml.Application.Current.Resources[isOutOfStock ? "SalesAmberBrush" : "AppTextBrush"];

    public static bool HasText(string? text) => !string.IsNullOrEmpty(text);

    // ───── lifecycle ─────

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadAsync(CancellationToken.None);
        GroupList.SelectedIndex = 0; // «همه گروه‌ها»
        SearchBox.Focus(FocusState.Programmatic);
    }

    private void OnGroupSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GroupList.SelectedItem is ProductGroupItem group)
        {
            ViewModel.SelectedGroup = group;
        }
    }

    // ───── actions ─────

    private void OnNewClick(object sender, RoutedEventArgs e) => OpenEditor(null);

    private void OnEditClick(object sender, RoutedEventArgs e) => OpenSelectedForEdit();

    private void OnRowDoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => OpenSelectedForEdit();

    private void OnExitClick(object sender, RoutedEventArgs e) => GoBack();

    private void OnStockCardClick(object sender, RoutedEventArgs e) => OpenStockCard();

    private void OpenStockCard()
    {
        if (ViewModel.SelectedItem is { } item)
        {
            Frame.Navigate(typeof(ERP.Desktop.Features.Inventory.StockCardPage), item.Row);
        }
    }

    private void OpenSelectedForEdit()
    {
        if (ViewModel.SelectedItem is { } item)
        {
            OpenEditor(item.Row);
        }
    }

    private void OpenEditor(ERP.Application.Catalog.ProductListRow? row) =>
        Frame.Navigate(typeof(ProductEditorPage), row);

    private void GoBack()
    {
        if (Frame.CanGoBack)
        {
            Frame.GoBack();
        }
    }

    // ───── keyboard (checklist «م»: F2 جدید · F3 ویرایش · F4 حذف · Esc خروج) ─────

    private void OnPagePreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (ViewModel.IsArchiveConfirmOpen)
        {
            if (e.Key == VirtualKey.Escape)
            {
                ViewModel.CancelArchiveCommand.Execute(null);
                e.Handled = true;
            }

            return; // while the question is open, nothing else on the screen reacts
        }

        switch (e.Key)
        {
            case VirtualKey.F2:
                OpenEditor(null);
                break;
            case VirtualKey.F3:
                // the search box is what F3 means on the sales screen too; on this screen
                // F3 is «ویرایش» when a row is chosen and the search box has no focus
                if (SearchBox.FocusState == FocusState.Unfocused && ViewModel.HasSelection)
                {
                    OpenSelectedForEdit();
                }
                else
                {
                    SearchBox.Focus(FocusState.Keyboard);
                    SearchBox.SelectAll();
                }

                break;
            case VirtualKey.F4:
                ViewModel.ArchiveCommand.Execute(null);
                break;
            case VirtualKey.F7:
                OpenStockCard();
                break;
            case VirtualKey.Enter when ProductList.FocusState != FocusState.Unfocused:
                OpenSelectedForEdit();
                break;
            case VirtualKey.Escape:
                GoBack();
                break;
            default:
                return;
        }

        e.Handled = true;
    }
}
