using System.ComponentModel;
using ERP.Presentation.Features.Inventory;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace ERP.Desktop.Features.Inventory;

/// <summary>
/// لیست انبارها. The page owns no rules: <see cref="WarehouseListViewModel"/> reads, saves and
/// archives; this only turns the bottom bar's keys (F2/F3/F4/Esc) into commands, moves focus into
/// the form when it opens, and goes back — the same behaviour as «لیست مشتریان».
/// </summary>
public sealed partial class WarehouseListPage : Page
{
    public WarehouseListPage()
    {
        ViewModel = new WarehouseListViewModel(
            App.Services.RetailSetup,
            App.Services.RetailSetup,
            App.Services.RetailSetup,
            App.Services.RetailSetup)
        {
            ReportUnexpectedError = App.TryLogCrash,
        };

        InitializeComponent();
        Loaded += OnLoaded;
        PreviewKeyDown += OnPagePreviewKeyDown;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    public WarehouseListViewModel ViewModel { get; }

    // ───── x:Bind helpers ─────

    public static bool HasText(string? text) => !string.IsNullOrEmpty(text);

    // ───── lifecycle ─────

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadAsync(CancellationToken.None);
        SearchBox.Focus(FocusState.Programmatic);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WarehouseListViewModel.IsEditorOpen))
        {
            // the form takes the keyboard when it opens; the list gets it back when it closes
            DispatcherQueue.TryEnqueue(() =>
            {
                if (ViewModel.IsEditorOpen)
                {
                    NameBox.Focus(FocusState.Programmatic);
                }
                else
                {
                    WarehouseList.Focus(FocusState.Programmatic);
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
            case VirtualKey.Enter when WarehouseList.FocusState != FocusState.Unfocused:
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
