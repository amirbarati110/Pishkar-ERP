using ERP.Application.Identity;
using ERP.Presentation.Features.Identity;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ERP.Desktop;

/// <summary>
/// Shown before <see cref="MainWindow"/> — either the one-time «راه‌اندازی
/// اولیه» (no user exists yet) or the normal «ورود» screen (milestone-1
/// build order step 4, §10). Both view models are always built so x:Bind has
/// a non-null target either way; only one panel is visible at a time.
/// </summary>
public sealed partial class AuthWindow : Window
{
    public SetupViewModel SetupVm { get; }

    public LoginViewModel LoginVm { get; }

    public event Action<SignedInUser>? Completed;

    public AuthWindow(IRegisterFirstAdminHandler registerFirstAdmin, ISignInHandler signIn, bool needsSetup)
    {
        SetupVm = new SetupViewModel(registerFirstAdmin);
        LoginVm = new LoginViewModel(signIn);
        InitializeComponent();

        SetupVm.Succeeded += user => Completed?.Invoke(user);
        LoginVm.Succeeded += user => Completed?.Invoke(user);

        SetupPanel.Visibility = needsSetup ? Visibility.Visible : Visibility.Collapsed;
        LoginPanel.Visibility = needsSetup ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnSetupPasswordChanged(object sender, RoutedEventArgs e) => SetupVm.Password = SetupPasswordBox.Password;

    private void OnSetupConfirmPasswordChanged(object sender, RoutedEventArgs e) => SetupVm.ConfirmPassword = SetupConfirmPasswordBox.Password;

    private void OnLoginPasswordChanged(object sender, RoutedEventArgs e) => LoginVm.Password = LoginPasswordBox.Password;

    public static Visibility WhenText(string? text) => string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;

    public static bool WhenNotBusy(bool isBusy) => !isBusy;
}
