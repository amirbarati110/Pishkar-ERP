using ERP.Presentation.Features.Inventory;
using Microsoft.UI.Xaml.Controls;

namespace ERP.Desktop.Features.Inventory;

public sealed partial class OpeningStockPage : Page
{
    public OpeningStockPage()
    {
        ViewModel = new OpeningStockViewModel(
            App.Services.RetailSetup,
            App.Services.CatalogLookup)
        {
            WarehouseId = App.Services.Defaults.MainWarehouseId,
        };
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public OpeningStockViewModel ViewModel { get; }

    private async void OnLoaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await ViewModel.LoadAsync(CancellationToken.None);
    }
}
