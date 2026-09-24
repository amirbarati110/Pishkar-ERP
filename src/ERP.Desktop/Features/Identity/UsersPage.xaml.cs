using System.ComponentModel;
using ERP.Presentation.Features.Identity;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace ERP.Desktop.Features.Identity;

/// <summary>
/// کاربران. The page owns no rules: <see cref="UsersViewModel"/> reads, saves, resets a password
/// and archives; this only turns the bottom bar's keys (F2/F3/F4/Esc) into commands, moves focus
/// into whichever form is open, and goes back — the same behaviour as «لیست انبارها». Reachable
/// only through <see cref="ERP.Presentation.Features.Navigation.NavigationMap"/>'s manager-only
/// gate, so by the time this page opens the acting user already holds <c>ManageUsers</c>.
/// </summary>
public sealed partial class UsersPage : Page
{
    public UsersPage()
    {
        ViewModel = new UsersViewModel(
            App.CurrentUser.UserId,
            App.Services.Identity,
            App.Services.Identity,
            App.Services.Identity,
            App.Services.Identity,
            App.Services.Identity)
        {
            ReportUnexpectedError = App.TryLogCrash,
        };

        InitializeComponent();
        Loaded += OnLoaded;
        PreviewKeyDown += OnPagePreviewKeyDown;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    public UsersViewModel ViewModel { get; }

    // ───── x:Bind helpers ─────

    public static bool HasText(string? text) => !string.IsNullOrEmpty(text);

    public static double RowOpacity(bool isArchived) => isArchived ? 0.55 : 1.0;

    // ───── lifecycle ─────

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadAsync(CancellationToken.None);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UsersViewModel.IsEditorOpen) && ViewModel.IsEditorOpen)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                NewUserPasswordBox.Password = string.Empty;
                DisplayNameBox.Focus(FocusState.Programmatic);
            });
        }
        else if (e.PropertyName == nameof(UsersViewModel.IsResetPasswordOpen) && ViewModel.IsResetPasswordOpen)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                ResetPasswordBox.Password = string.Empty;
                ResetPasswordBox.Focus(FocusState.Programmatic);
            });
        }
        else if (e.PropertyName == nameof(UsersViewModel.IsEditorOpen) && !ViewModel.IsEditorOpen)
        {
            DispatcherQueue.TryEnqueue(() => UserList.Focus(FocusState.Programmatic));
        }
    }

    // ───── actions ─────

    private void OnRowDoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => ViewModel.EditCommand.Execute(null);

    private void OnPasswordChanged(object sender, RoutedEventArgs e) => ViewModel.Password = NewUserPasswordBox.Password;

    private void OnResetPasswordChanged(object sender, RoutedEventArgs e) => ViewModel.NewPassword = ResetPasswordBox.Password;

    private void OnExitClick(object sender, RoutedEventArgs e) => GoBack();

    private void GoBack()
    {
        if (Frame.CanGoBack)
        {
            Frame.GoBack();
        }
    }

    // ───── keyboard (F2 جدید · F3 ویرایش · F4 حذف · Esc خروج — same as the other lists) ─────

    private void OnPagePreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (ViewModel.IsArchiveConfirmOpen)
        {
            if (e.Key == VirtualKey.Escape)
            {
                ViewModel.CancelArchiveCommand.Execute(null);
                e.Handled = true;
            }

            return;
        }

        if (ViewModel.IsResetPasswordOpen)
        {
            switch (e.Key)
            {
                case VirtualKey.Escape:
                    ViewModel.CancelResetPasswordCommand.Execute(null);
                    e.Handled = true;
                    break;
                case VirtualKey.Enter:
                    ViewModel.ConfirmResetPasswordCommand.Execute(null);
                    e.Handled = true;
                    break;
            }

            return;
        }

        if (ViewModel.IsEditorOpen)
        {
            switch (e.Key)
            {
                case VirtualKey.Escape:
                    ViewModel.CancelEditorCommand.Execute(null);
                    e.Handled = true;
                    break;
                case VirtualKey.Enter:
                    ViewModel.SaveCommand.Execute(null);
                    e.Handled = true;
                    break;
            }

            return;
        }

        switch (e.Key)
        {
            case VirtualKey.F2:
                ViewModel.NewCommand.Execute(null);
                break;
            case VirtualKey.F3:
                if (ViewModel.HasSelection)
                {
                    ViewModel.EditCommand.Execute(null);
                }

                break;
            case VirtualKey.F4:
                ViewModel.ArchiveCommand.Execute(null);
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
