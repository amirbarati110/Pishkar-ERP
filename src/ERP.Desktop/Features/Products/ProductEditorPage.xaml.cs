using ERP.Presentation.Features.Products;
using Microsoft.UI.Xaml.Controls;

namespace ERP.Desktop.Features.Products;

public sealed partial class ProductEditorPage : Page
{
    public ProductEditorPage()
    {
        ViewModel = new ProductEditorViewModel(
            App.Services.RetailSetup,
            App.Services.CatalogLookup);
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public ProductEditorViewModel ViewModel { get; }

    private async void OnLoaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await ViewModel.LoadAsync(
            App.Services.Defaults.GeneralCategoryId,
            App.Services.Defaults.EachUnitId,
            CancellationToken.None);
    }
}
