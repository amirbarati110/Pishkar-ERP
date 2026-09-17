using ERP.Presentation.Features.Cashiering;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ERP.Desktop.Features.Cashiering;

public sealed partial class CashShiftPage : Page
{
    public CashShiftPage()
    {
        ViewModel = new CashShiftViewModel(
            App.Services.Cashiering,
            App.Services.Cashiering,
            App.Services.Cashiering,
            App.Services.Defaults.MainWarehouseId);
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.LoadAsync(CancellationToken.None);
    }

    public CashShiftViewModel ViewModel { get; }

    public static Visibility WhenFalse(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility WhenText(string? text) => string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
}
