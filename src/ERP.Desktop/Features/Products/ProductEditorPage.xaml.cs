using ERP.Application.Catalog;
using ERP.Presentation.Features.Products;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;

namespace ERP.Desktop.Features.Products;

/// <summary>
/// The product form. Opened from «لیست کالاها»: with no parameter it makes a new product
/// (F2 there), with a <see cref="ProductListRow"/> it edits that product (F3 there).
/// Keys: Ctrl+S saves and goes back to the list, F5 saves and starts the next new product,
/// Esc goes back.
/// </summary>
public sealed partial class ProductEditorPage : Page
{
    public ProductEditorPage()
    {
        ViewModel = new ProductEditorViewModel(
            App.Services.RetailSetup,
            App.Services.CatalogLookup,
            App.Services.RetailSetup,
            App.Services.RetailSetup);
        ViewModel.SavedAndClosed += (_, _) => GoBack();
        InitializeComponent();
        Loaded += OnLoaded;
        PreviewKeyDown += OnPagePreviewKeyDown;
    }

    public ProductEditorViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnNavigatedTo(e);

        if (e.Parameter is ProductListRow row)
        {
            ViewModel.BeginEdit(row);
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadAsync(
            App.Services.Defaults.GeneralCategoryId,
            App.Services.Defaults.EachUnitId,
            CancellationToken.None);

        // The boxes select by item, not by id: an id is a .NET struct, and XAML cannot compare
        // two of them (SelectedValue silently matched nothing and both boxes showed empty).
        CategoryBox.SelectedItem = ViewModel.Categories.FirstOrDefault(item => item.Id == ViewModel.CategoryId);
        UnitBox.SelectedItem = ViewModel.Units.FirstOrDefault(item => item.Id == ViewModel.BaseUnitId);
    }

    private void OnCategoryChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CategoryBox.SelectedItem is CategoryLookupItem item)
        {
            ViewModel.CategoryId = item.Id;
        }
    }

    private void OnUnitChanged(object sender, SelectionChangedEventArgs e)
    {
        if (UnitBox.SelectedItem is UnitLookupItem item)
        {
            ViewModel.BaseUnitId = item.Id;
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
        var control = Microsoft.UI.Input.InputKeyboardSource
            .GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        if (e.Key == VirtualKey.Escape)
        {
            GoBack();
        }
        else if (control && e.Key == VirtualKey.S)
        {
            ViewModel.SaveAndReturnCommand.Execute(null);
        }
        else if (e.Key == VirtualKey.F5)
        {
            if (ViewModel.IsEditing)
            {
                ViewModel.SaveAndReturnCommand.Execute(null);
            }
            else
            {
                ViewModel.SaveAndNextCommand.Execute(null);
            }
        }
        else
        {
            return;
        }

        e.Handled = true;
    }
}

/// <summary>true ⇄ false, for «this control is only usable while creating».</summary>
public sealed class NotConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => value is not true;

    public object ConvertBack(object value, Type targetType, object parameter, string language) => value is not true;
}

/// <summary>true → collapsed, false → visible, for «this button only exists while creating».</summary>
public sealed class NotVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
