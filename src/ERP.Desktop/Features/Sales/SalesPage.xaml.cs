using System.ComponentModel;
using ERP.Application.Customers;
using ERP.Application.Sales;
using ERP.Domain.Common;
using ERP.Domain.Sales;
using ERP.Presentation.Features.Sales;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace ERP.Desktop.Features.Sales;

/// <summary>
/// The sales workspace page. Everything the cashier does is a command on
/// <see cref="SalesWorkspaceViewModel"/>; this file only turns clicks, keys and
/// window size into those commands, and picks brushes for visual states.
/// </summary>
public sealed partial class SalesPage : Page
{
    /// <summary>Below this width the category column moves above the product list (as in the approved mockup's breakpoint).</summary>
    private const double NarrowWidth = 1100;

    /// <summary>Navigation parameter: open the workspace on a clean invoice (the default).</summary>
    public const string StartFresh = "new";

    /// <summary>Navigation parameter: open the workspace with the invoice list (F2) already up, to continue a held invoice.</summary>
    public const string StartOnInvoiceList = "held";

    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(20) };
    private readonly DispatcherTimer _noticeTimer = new() { Interval = TimeSpan.FromSeconds(4) };

    public SalesPage()
    {
        ViewModel = new SalesWorkspaceViewModel(App.Services.SalesBackend, App.Services.Defaults.MainWarehouseId);
        InitializeComponent();

        ViewModel.ReportUnexpectedError = App.TryLogCrash;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        _clock.Tick += (_, _) => UpdateClock();
        _noticeTimer.Tick += (_, _) =>
        {
            _noticeTimer.Stop();
            NoticeBar.IsOpen = false;
        };

        Loaded += OnLoaded;
        Unloaded += (_, _) =>
        {
            _clock.Stop();
            _noticeTimer.Stop();
        };
        SizeChanged += (_, args) => ApplyLayout(args.NewSize.Width);
        PreviewKeyDown += OnPagePreviewKeyDown;
        CharacterReceived += OnPageCharacterReceived;
    }

    public SalesWorkspaceViewModel ViewModel { get; }

    private bool _openInvoiceListOnLoad;

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnNavigatedTo(e);
        _openInvoiceListOnLoad = e.Parameter as string == StartOnInvoiceList;
    }

    // ───── x:Bind helpers (visual states) ─────

    public static Brush TabBackground(bool isCurrent) => isCurrent ? Token("AppSurfaceBrush") : new SolidColorBrush(Microsoft.UI.Colors.Transparent);

    public static Thickness TabBorder(bool isCurrent) => isCurrent ? new Thickness(1, 1, 1, 0) : new Thickness(0);

    public static Windows.UI.Text.FontWeight TabWeight(bool isCurrent) => isCurrent ? FontWeights.Bold : FontWeights.SemiBold;

    public static Brush ChipBackground(bool isSelected) => Token(isSelected ? "SalesPrimaryDarkBrush" : "AppSurfaceBrush");

    public static Brush ChipForeground(bool isSelected) => isSelected ? new SolidColorBrush(Microsoft.UI.Colors.White) : Token("SalesMutedTextBrush");

    public static Brush StockBrush(StockLevel level) => Token(level switch
    {
        StockLevel.Out => "SalesRedBrush",
        StockLevel.Low => "SalesAmberBrush",
        _ => "AppTextBrush",
    });

    /// <summary>
    /// A steady colour per product tile, so the same product always looks the
    /// same on every run (string.GetHashCode is randomised per process).
    /// </summary>
    public static Brush TileColor(string name)
    {
        string[] palette = ["AppPrimaryBrush", "SalesBlueBrush", "SalesBrownBrush", "AppAccentBrush", "SalesPurpleBrush"];
        var sum = name?.Sum(character => character) ?? 0;
        return Token(palette[sum % palette.Length]);
    }

    public static Visibility WhenZero(int count) => count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility WhenText(string? text) => string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility WhenFalse(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    private static Brush Token(string key) => (Brush)Microsoft.UI.Xaml.Application.Current.Resources[key];

    // ───── lifecycle ─────

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateClock();
        _clock.Start();
        ApplyLayout(ActualWidth);
        UpdateFilterButtons();
        await ViewModel.LoadAsync(CancellationToken.None);

        if (_openInvoiceListOnLoad)
        {
            // Came from «فاکتورهای معلق» on the workbench: land on the list
            // instead of making the cashier press F2 again.
            _openInvoiceListOnLoad = false;
            ViewModel.OpenInvoiceListCommand.Execute(null);
            return;
        }

        ProductSearchBox.Focus(FocusState.Programmatic);
    }

    private void UpdateClock()
    {
        var now = App.Services.SalesBackend.Clock.UtcNow;
        ClockText.Text = SalesText.ClockTime(now);
        DateText.Text = SalesText.LongDate(PersianDate.FromUtc(now));
    }

    private void ApplyLayout(double width)
    {
        var narrow = width < NarrowWidth;
        CategoriesColumn.Width = narrow ? new GridLength(0) : new GridLength(0.82, GridUnitType.Star);
        WorkTopRow.Height = narrow ? new GridLength(170) : new GridLength(1, GridUnitType.Star);
        WorkBottomRow.Height = narrow ? new GridLength(1, GridUnitType.Star) : new GridLength(0);

        Grid.SetRow(CategoriesCard, 0);
        Grid.SetColumn(CategoriesCard, narrow ? 1 : 0);
        Grid.SetColumnSpan(CategoriesCard, narrow ? 2 : 1);

        Grid.SetRow(ProductsCard, narrow ? 1 : 0);
        Grid.SetRow(InvoiceCard, narrow ? 1 : 0);

        ClockPanel.Visibility = width < 860 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(SalesWorkspaceViewModel.Notice) when ViewModel.Notice is { } message:
                NoticeBar.Severity = ViewModel.NoticeIsError ? InfoBarSeverity.Error : InfoBarSeverity.Success;
                NoticeBar.Message = message;
                NoticeBar.IsOpen = true;
                _noticeTimer.Stop();
                _noticeTimer.Start();
                break;
            case nameof(SalesWorkspaceViewModel.ProductFilter):
                UpdateFilterButtons();
                break;
            case nameof(SalesWorkspaceViewModel.SelectedPaymentMethod):
                UpdatePaymentButtons();
                break;
            case nameof(SalesWorkspaceViewModel.IsPaymentOpen) when ViewModel.IsPaymentOpen:
                UpdatePaymentButtons();
                DispatcherQueue.TryEnqueue(() =>
                {
                    CashReceivedBox.Focus(FocusState.Programmatic);
                    CashReceivedBox.SelectAll();
                });
                break;
            case nameof(SalesWorkspaceViewModel.IsCompletedSummaryOpen) when ViewModel.IsCompletedSummaryOpen:
                DispatcherQueue.TryEnqueue(() => StartNextButton.Focus(FocusState.Programmatic));
                break;
            case nameof(SalesWorkspaceViewModel.IsCancelConfirmOpen) when ViewModel.IsCancelConfirmOpen:
                DispatcherQueue.TryEnqueue(() => KeepInvoiceButton.Focus(FocusState.Programmatic));
                break;
            case nameof(SalesWorkspaceViewModel.IsNewCustomerOpen) when ViewModel.IsNewCustomerOpen:
                DispatcherQueue.TryEnqueue(() => NewCustomerMobileBox.Focus(FocusState.Programmatic));
                break;
            case nameof(SalesWorkspaceViewModel.IsBusy) when !ViewModel.IsBusy && !IsAnyWindowOpen && FocusManager.GetFocusedElement(XamlRoot) is not TextBox:
                ProductSearchBox.Focus(FocusState.Programmatic);
                break;
        }
    }

    private bool IsAnyWindowOpen =>
        ViewModel.IsPaymentOpen || ViewModel.IsEditLineOpen || ViewModel.IsNewCustomerOpen
        || ViewModel.IsInvoiceListOpen || ViewModel.IsCancelConfirmOpen || ViewModel.IsCompletedSummaryOpen;

    private void UpdateFilterButtons()
    {
        foreach (var (button, filter) in new[]
        {
            (FilterAllButton, ProductListFilter.All),
            (FilterTopButton, ProductListFilter.TopSelling),
            (FilterLowButton, ProductListFilter.LowStock),
        })
        {
            var selected = ViewModel.ProductFilter == filter;
            button.Background = ChipBackground(selected);
            button.Foreground = ChipForeground(selected);
        }
    }

    private void UpdatePaymentButtons()
    {
        foreach (var (button, method) in new[]
        {
            (PayCashButton, PaymentMethod.Cash),
            (PayCardButton, PaymentMethod.Card),
            (PayCreditButton, PaymentMethod.Credit),
            (PayChequeButton, PaymentMethod.Cheque),
        })
        {
            var selected = ViewModel.SelectedPaymentMethod == method;
            button.Background = Token(selected ? "AppPrimarySoftBrush" : "AppSurfaceBrush");
            button.BorderBrush = Token(selected ? "AppPrimaryBrush" : "SalesLineBrush");
            button.Foreground = Token(selected ? "SalesPrimaryDarkBrush" : "SalesMutedTextBrush");
        }
    }

    // ───── keyboard (SalesShortcuts is the one table of keys) ─────

    private void OnPagePreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var control = IsDown(VirtualKey.Control);
        var alt = IsDown(VirtualKey.Menu);

        if (ViewModel.IsPaymentOpen && alt && e.Key is >= VirtualKey.Number1 and <= VirtualKey.Number4)
        {
            ViewModel.SelectPaymentMethodCommand.Execute(ViewModel.PaymentMethods[e.Key - VirtualKey.Number1]);
            e.Handled = true;
            return;
        }

        var shortcut = SalesShortcuts.All.FirstOrDefault(item =>
            item.Control == control && Enum.TryParse<VirtualKey>(item.Key, out var key) && key == e.Key);
        if (shortcut is null)
        {
            if (e.Key == VirtualKey.Enter && ViewModel.IsCompletedSummaryOpen)
            {
                ViewModel.StartNextAfterSummaryCommand.Execute(null);
                e.Handled = true;
            }

            return;
        }

        e.Handled = true;
        if (shortcut == SalesShortcuts.CloseDialog)
        {
            // §3.14 «Esc برای Back/Close»: Esc closes whatever window is open,
            // and when none is, it is Back — the same trip the «بازگشت به میز کار»
            // button makes. A half-finished invoice is not lost by leaving: a
            // draft with items stays held (§6.13).
            if (IsAnyWindowOpen)
            {
                ViewModel.CloseTopDialogCommand.Execute(null);
            }
            else
            {
                GoBackToWorkbench();
            }

            return;
        }

        if (IsAnyWindowOpen)
        {
            return; // screen shortcuts wait until the open window is closed
        }

        if (shortcut == SalesShortcuts.InvoiceList)
        {
            ViewModel.OpenInvoiceListCommand.Execute(null);
        }
        else if (shortcut == SalesShortcuts.FocusSearch)
        {
            ProductSearchBox.Focus(FocusState.Keyboard);
            ProductSearchBox.SelectAll();
        }
        else if (shortcut == SalesShortcuts.CompleteAndNext)
        {
            ViewModel.OpenPaymentCommand.Execute(CompletionFollowUp.NextInvoice);
        }
        else if (shortcut == SalesShortcuts.ReceivePayment)
        {
            ViewModel.OpenPaymentCommand.Execute(CompletionFollowUp.ShowSummary);
        }
        else if (shortcut == SalesShortcuts.NewInvoice)
        {
            ViewModel.NewInvoiceCommand.Execute(null);
        }
        else if (shortcut == SalesShortcuts.CompleteAndPrint)
        {
            ViewModel.ShowNotSoonNotice("چاپ فاکتور در قدم بعدی ساخته می‌شود؛ برای ثبت از F6 استفاده کنید.");
        }
    }

    /// <summary>
    /// §6.8: a scanner must not need the cashier to click into the search box
    /// first. Typing that lands nowhere useful is sent to the product search.
    /// </summary>
    private void OnPageCharacterReceived(UIElement sender, CharacterReceivedRoutedEventArgs args)
    {
        if (IsAnyWindowOpen || char.IsControl(args.Character))
        {
            return;
        }

        var focused = FocusManager.GetFocusedElement(XamlRoot);
        if (focused is TextBox or AutoSuggestBox or PasswordBox)
        {
            return;
        }

        ProductSearchBox.Focus(FocusState.Programmatic);
        ProductSearchBox.Text += args.Character;
        ProductSearchBox.SelectionStart = ProductSearchBox.Text.Length;
        args.Handled = true;
    }

    private static bool IsDown(VirtualKey key) =>
        Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    private void OnProductSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            ViewModel.SubmitProductSearchCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnFooterKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && sender is TextBox box)
        {
            CommitFooter(box);
            e.Handled = true;
        }
    }

    private void OnPaymentKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            if (ViewModel.NeedsCreditApproval)
            {
                return; // approval is a deliberate click, not an Enter
            }

            ViewModel.ConfirmPaymentCommand.Execute(null);
            e.Handled = true;
        }
    }

    // ───── clicks ─────

    /// <summary>
    /// The row a template button belongs to. Read from Tag="{x:Bind}", not from
    /// DataContext: inside x:Bind templates — ItemsRepeater in particular — a
    /// button's DataContext is not guaranteed to be the item, and relying on it
    /// crashed the page when a product tile was clicked.
    /// </summary>
    private static T? ItemOf<T>(object sender)
        where T : class =>
        (sender as FrameworkElement)?.Tag as T ?? (sender as FrameworkElement)?.DataContext as T;

    private static void RunWithItem<T>(object sender, System.Windows.Input.ICommand command)
        where T : class
    {
        if (ItemOf<T>(sender) is { } item)
        {
            command.Execute(item);
        }
    }

    private void OnExitClick(object sender, RoutedEventArgs e) => GoBackToWorkbench();

    private void GoBackToWorkbench()
    {
        if (Frame.CanGoBack)
        {
            Frame.GoBack();
        }
        else
        {
            Frame.Navigate(typeof(MainPage));
        }
    }

    private void OnTabTapped(object sender, TappedRoutedEventArgs e) =>
        RunWithItem<InvoiceTab>(sender, ViewModel.SelectTabCommand);

    private void OnCloseTabClick(object sender, RoutedEventArgs e)
    {
        if (ItemOf<InvoiceTab>(sender) is { } tab)
        {
            ViewModel.CloseTabCommand.Execute(tab);
        }
    }

    private void OnCategoryClick(object sender, RoutedEventArgs e) =>
        RunWithItem<CategoryChip>(sender, ViewModel.SelectCategoryCommand);

    private void OnProductClick(object sender, RoutedEventArgs e) =>
        RunWithItem<ProductRow>(sender, ViewModel.AddProductCommand);

    private void OnProductListItemClick(object sender, ItemClickEventArgs e)
        { if (e.ClickedItem is ProductRow product) { ViewModel.AddProductCommand.Execute(product); } }

    private void OnFocusSearchClick(object sender, RoutedEventArgs e) => ProductSearchBox.Focus(FocusState.Programmatic);

    private void OnFilterClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag } && Enum.TryParse<ProductListFilter>(tag, out var filter))
        {
            ViewModel.SetProductFilterCommand.Execute(filter);
        }
    }

    private void OnPreviousProductPageClick(object sender, RoutedEventArgs e) => ViewModel.ChangeProductPageCommand.Execute(-1);

    private void OnNextProductPageClick(object sender, RoutedEventArgs e) => ViewModel.ChangeProductPageCommand.Execute(1);

    private void OnEditLineClick(object sender, RoutedEventArgs e) =>
        RunWithItem<CartLineRow>(sender, ViewModel.OpenEditLineCommand);

    private void OnRemoveLineClick(object sender, RoutedEventArgs e) =>
        RunWithItem<CartLineRow>(sender, ViewModel.RemoveLineCommand);

    private void OnIncreaseLineClick(object sender, RoutedEventArgs e) =>
        RunWithItem<CartLineRow>(sender, ViewModel.IncreaseLineCommand);

    private void OnDecreaseLineClick(object sender, RoutedEventArgs e) =>
        RunWithItem<CartLineRow>(sender, ViewModel.DecreaseLineCommand);

    private void OnChargesLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox box)
        {
            CommitFooter(box);
        }
    }

    private void OnTaxLostFocus(object sender, RoutedEventArgs e) => ViewModel.CommitTaxRateCommand.Execute(null);

    private void CommitFooter(TextBox box)
    {
        if (box.Tag as string == "tax")
        {
            ViewModel.CommitTaxRateCommand.Execute(null);
        }
        else
        {
            ViewModel.CommitChargesCommand.Execute(box.Tag as string);
        }
    }

    private void OnSaveAndNextClick(object sender, RoutedEventArgs e) =>
        ViewModel.OpenPaymentCommand.Execute(CompletionFollowUp.NextInvoice);

    private void OnReceivePaymentClick(object sender, RoutedEventArgs e) =>
        ViewModel.OpenPaymentCommand.Execute(CompletionFollowUp.ShowSummary);

    private void OnPaymentMethodClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag } && Enum.TryParse<PaymentMethod>(tag, out var method))
        {
            ViewModel.SelectPaymentMethodCommand.Execute(method);
        }
    }

    private void OnCustomerTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            ViewModel.CustomerSearchText = sender.Text;
        }
    }

    private void OnCustomerChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is CustomerSearchResult customer)
        {
            ViewModel.ChooseCustomerCommand.Execute(customer);
            sender.Text = string.Empty;
        }
    }

    private void OnHeldInvoiceClick(object sender, ItemClickEventArgs e)
        { if (e.ClickedItem is InvoiceListRow row) { ViewModel.ResumeHeldInvoiceCommand.Execute(row); } }
}
