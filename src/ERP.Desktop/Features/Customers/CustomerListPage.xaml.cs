using System.ComponentModel;
using ERP.Presentation.Features.Customers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace ERP.Desktop.Features.Customers;

/// <summary>
/// لیست مشتریان. The page owns no rules: <see cref="CustomerListViewModel"/> reads, filters,
/// saves and archives; this only turns the bottom bar's keys (F2/F3/F4/Esc) into commands,
/// moves focus into the form when it opens, and goes back.
/// </summary>
public sealed partial class CustomerListPage : Page
{
    public CustomerListPage()
    {
        ViewModel = new CustomerListViewModel(
            App.Services.CustomerList,
            App.Services.CustomerCreate,
            App.Services.CustomerUpdate,
            App.Services.CustomerArchive)
        {
            ReportUnexpectedError = App.TryLogCrash,
        };

        InitializeComponent();
        Loaded += OnLoaded;
        PreviewKeyDown += OnPagePreviewKeyDown;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    public CustomerListViewModel ViewModel { get; }

    // ───── x:Bind helpers ─────

    public static Brush BalanceBrush(bool isDebtor) =>
        (Brush)Microsoft.UI.Xaml.Application.Current.Resources[isDebtor ? "SalesAmberBrush" : "AppTextBrush"];

    public static bool HasText(string? text) => !string.IsNullOrEmpty(text);

    // ───── lifecycle ─────

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadAsync(CancellationToken.None);
        SearchBox.Focus(FocusState.Programmatic);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CustomerListViewModel.IsEditorOpen))
        {
            // the form takes the keyboard when it opens; the list gets it back when it closes
            DispatcherQueue.TryEnqueue(() =>
            {
                if (ViewModel.IsEditorOpen)
                {
                    FirstNameBox.Focus(FocusState.Programmatic);
                }
                else
                {
                    CustomerList.Focus(FocusState.Programmatic);
                }
            });
        }
    }

    // ───── actions ─────

    private void OnRowDoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => ViewModel.EditCommand.Execute(null);

    private void OnExitClick(object sender, RoutedEventArgs e) => GoBack();

    private void GoBack()
    {
        if (Frame.CanGoBack)
        {
            Frame.GoBack();
        }
    }

    // ───── keyboard (checklist «ن»: F2 جدید · F3 ویرایش · F4 حذف · Esc خروج) ─────

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

            return; // the form is modal: the list behind it does not react to F-keys
        }

        switch (e.Key)
        {
            case VirtualKey.F2:
                ViewModel.NewCommand.Execute(null);
                break;
            case VirtualKey.F3:
                // the search box is what F3 means on the sales screen too; on this screen
                // F3 is «ویرایش» when a row is chosen and the search box has no focus
                if (SearchBox.FocusState == FocusState.Unfocused && ViewModel.HasSelection)
                {
                    ViewModel.EditCommand.Execute(null);
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
            case VirtualKey.Enter when CustomerList.FocusState != FocusState.Unfocused:
                ViewModel.EditCommand.Execute(null);
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
