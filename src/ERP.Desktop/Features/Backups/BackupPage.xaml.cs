using ERP.Presentation.Features.Backups;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ERP.Desktop.Features.Backups;

public sealed partial class BackupPage : Page
{
    public BackupPage()
    {
        ViewModel = new BackupViewModel(
            App.Services.Backups,
            App.Services.Backups,
            App.Services.Backups,
            App.Services.BackupDirectory);
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.LoadAsync(CancellationToken.None);
    }

    public BackupViewModel ViewModel { get; }

    public static Visibility WhenFalse(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility WhenText(string? text) => string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;

    public static string DatabaseStatusText(bool reachable) => reachable ? "در دسترس" : "در دسترس نیست";

    public static Brush DatabaseStatusBrush(bool reachable) => reachable
        ? (Brush)Microsoft.UI.Xaml.Application.Current.Resources["AppSuccessBrush"]
        : (Brush)Microsoft.UI.Xaml.Application.Current.Resources["AppErrorBrush"];
}
