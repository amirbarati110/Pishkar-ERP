using ERP.Presentation.Features.Backups;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;

namespace ERP.Desktop.Features.Backups;

public sealed partial class BackupPage : Page
{
    public BackupPage()
    {
        ViewModel = new BackupViewModel(
            App.Services.Backups,
            App.Services.Backups,
            App.Services.Backups,
            App.Services.Backups,
            App.Services.BackupDirectory);
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.LoadAsync(CancellationToken.None);
    }

    public BackupViewModel ViewModel { get; }

    /// <summary>«بازیابی از فایل…» — e.g. a backup brought on a USB disk to a freshly installed PC.</summary>
    private async void OnRestoreFromFileClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeFilter.Add(".fbk");

        var file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            ViewModel.BeginRestoreFromFile(file.Path);
        }
    }

    private void OnAdminPasswordChanged(object sender, RoutedEventArgs e) => ViewModel.AdminPassword = AdminPasswordBox.Password;

    /// <summary>After a restore every screen holds the old data; reopening the app is the only honest state.</summary>
    private void OnCloseAppClick(object sender, RoutedEventArgs e) => Microsoft.UI.Xaml.Application.Current.Exit();

    public static Visibility WhenFalse(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility WhenText(string? text) => string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;

    public static string DatabaseStatusText(bool reachable) => reachable ? "در دسترس" : "در دسترس نیست";

    public static Brush DatabaseStatusBrush(bool reachable) => reachable
        ? (Brush)Microsoft.UI.Xaml.Application.Current.Resources["AppSuccessBrush"]
        : (Brush)Microsoft.UI.Xaml.Application.Current.Resources["AppErrorBrush"];
}
