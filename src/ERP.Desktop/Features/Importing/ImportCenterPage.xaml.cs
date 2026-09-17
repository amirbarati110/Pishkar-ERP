using ERP.Presentation.Features.Importing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;
using Windows.UI;

namespace ERP.Desktop.Features.Importing;

public sealed partial class ImportCenterPage : Page
{
    public ImportCenterPage()
    {
        ViewModel = new ImportCenterViewModel(
            App.Services.SpreadsheetReader,
            App.Services.CatalogLookup,
            App.Services.RetailSetup,
            App.Services.Defaults.MainWarehouseId);
        InitializeComponent();
    }

    public ImportCenterViewModel ViewModel { get; }

    private async void OnChooseFileClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeFilter.Add(".xlsx");

        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        await using var stream = await file.OpenStreamForReadAsync();
        await ViewModel.LoadFileAsync(file.Name, stream, CancellationToken.None);
    }

    public static Visibility WhenText(string? text) => string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;

    public static Brush ErrorForeground(bool hasError) => hasError
        ? new SolidColorBrush(Color.FromArgb(0xFF, 0xC8, 0x37, 0x2F))
        : (Brush)Microsoft.UI.Xaml.Application.Current.Resources["AppTextBrush"];
}
