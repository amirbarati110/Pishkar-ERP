using System.ComponentModel;
using ERP.Application.Customers;
using ERP.Application.Sales;
using ERP.Domain.Common;
using ERP.Domain.Customers;
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
/// <summary>Navigation parameter: open the workspace on the completed invoice with this number (from a row of a product's کاردکس).</summary>
public sealed record OpenInvoiceRequest(long Number);

public sealed partial class SalesPage : Page
{
    /// <summary>Below this width the category column moves above the product list (as in the approved mockup's breakpoint).</summary>
    private const double NarrowWidth = 1100;

    /// <summary>Navigation parameter: open the workspace on a clean invoice (the default).</summary>
    public const string StartFresh = "new";

    /// <summary>Navigation parameter: open the workspace with the invoice list (F2) already up, to continue a held invoice.</summary>
    public const string StartOnInvoiceList = "held";

    /// <summary>Navigation parameter: open the workspace with the return window (§6.25) already up.</summary>
    public const string StartOnReturn = "return";

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
    private bool _openReturnOnLoad;
    private long? _openInvoiceNumberOnLoad;

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnNavigatedTo(e);
        _openInvoiceListOnLoad = e.Parameter as string == StartOnInvoiceList;
        _openReturnOnLoad = e.Parameter as string == StartOnReturn;
        _openInvoiceNumberOnLoad = (e.Parameter as OpenInvoiceRequest)?.Number;
    }

    // ───── x:Bind helpers (visual states) ─────

    /// <summary>Whether «قیمت اصلی کالا هم به‌روز شود» is even offered — a master-price change is «ManageCatalog»'s (§15.2), not every cashier's.</summary>
    public static bool CanUpdateCatalogPrice => App.CurrentUser.Can(ERP.Domain.Identity.AccessRight.ManageCatalog);

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

    public static Visibility WhenNoText(string? text) => string.IsNullOrEmpty(text) ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>The two banners share one row (§10.10); the correction one wins when both would otherwise apply.</summary>
    public static Visibility WhenBalanceAndNotCorrection(string? balanceText, bool isCorrection) =>
        !isCorrection && !string.IsNullOrEmpty(balanceText) ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility WhenFalse(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility WhenReturnCanBeConfirmed(bool hasInvoice, bool isDone) =>
        hasInvoice && !isDone ? Visibility.Visible : Visibility.Collapsed;

    private static Brush Token(string key) => (Brush)Microsoft.UI.Xaml.Application.Current.Resources[key];

    // ───── lifecycle ─────

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateClock();
        _clock.Start();
        ApplyLayout(ActualWidth);
        UpdateFilterButtons();
        await ViewModel.LoadAsync(CancellationToken.None);

        if (_openInvoiceNumberOnLoad is { } invoiceNumber)
        {
            // Came from a row of a product's کاردکس: land on that invoice, not on an empty till.
            _openInvoiceNumberOnLoad = null;
            await ViewModel.OpenInvoiceByNumberCommand.ExecuteAsync(invoiceNumber);
            return;
        }

        if (_openReturnOnLoad)
        {
            _openReturnOnLoad = false;
            ViewModel.OpenReturnCommand.Execute(null);
            return;
        }

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
            case nameof(SalesWorkspaceViewModel.ReturnRefundMethod):
            case nameof(SalesWorkspaceViewModel.CanRefundToDebt):
                UpdateReturnRefundButtons();
                break;
            case nameof(SalesWorkspaceViewModel.IsReturnOpen) when ViewModel.IsReturnOpen:
                UpdateReturnRefundButtons();
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => ReturnNumberBox.Focus(FocusState.Programmatic));
                break;
            case nameof(SalesWorkspaceViewModel.ReceivePaymentMethod):
                UpdateReceivePaymentButtons();
                break;
            case nameof(SalesWorkspaceViewModel.IsReceivePaymentOpen) when ViewModel.IsReceivePaymentOpen:
                UpdateReceivePaymentButtons();
                DispatcherQueue.TryEnqueue(() =>
                {
                    ReceivePaymentAmountBox.Focus(FocusState.Programmatic);
                    ReceivePaymentAmountBox.SelectAll();
                });
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
        || ViewModel.IsInvoiceListOpen || ViewModel.IsCancelConfirmOpen || ViewModel.IsCompletedSummaryOpen
        || ViewModel.IsReceivePaymentOpen || ViewModel.IsStartingCorrectionOpen || ViewModel.IsJournalEntryOpen
        || ViewModel.IsReceiptPreviewOpen || ViewModel.IsReturnOpen || ViewModel.IsInvoiceViewOpen || HelpOverlay.Workflow is not null;

    // ───── راهنمای این صفحه (§4.1/§3.14) ─────

    private void OnHelpCloseRequested(object sender, EventArgs e) => HelpOverlay.Workflow = null;

    private void OnHelpButtonClick(object sender, RoutedEventArgs e)
    {
        if (HelpOverlay.Workflow is not null)
        {
            HelpOverlay.Workflow = null;
        }
        else if (!IsAnyWindowOpen)
        {
            HelpOverlay.Workflow = App.Workflows.Load("sales-workspace");
        }
    }

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

    private void UpdateReturnRefundButtons()
    {
        foreach (var (button, method) in new[]
        {
            (ReturnCashButton, PaymentMethod.Cash),
            (ReturnCardButton, PaymentMethod.Card),
            (ReturnDebtButton, PaymentMethod.Credit),
        })
        {
            var selected = ViewModel.ReturnRefundMethod == method;
            button.Background = Token(selected ? "AppPrimarySoftBrush" : "AppSurfaceBrush");
            button.BorderBrush = Token(selected ? "AppPrimaryBrush" : "SalesLineBrush");
            button.Foreground = Token(selected ? "SalesPrimaryDarkBrush" : "SalesMutedTextBrush");
        }
    }

    private void UpdateReceivePaymentButtons()
    {
        foreach (var (button, method) in new[]
        {
            (ReceiveCashButton, CustomerPaymentMethod.Cash),
            (ReceiveCardButton, CustomerPaymentMethod.Card),
        })
        {
            var selected = ViewModel.ReceivePaymentMethod == method;
            button.Background = Token(selected ? "AppPrimarySoftBrush" : "AppSurfaceBrush");
            button.BorderBrush = Token(selected ? "AppPrimaryBrush" : "SalesLineBrush");
            button.Foreground = Token(selected ? "SalesPrimaryDarkBrush" : "SalesMutedTextBrush");
        }
    }

    // ───── keyboard (SalesShortcuts is the one table of keys) ─────

    private void OnPagePreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.F1)
        {
            // §3.14/§4.1: F1 is «راهنمای این صفحه» everywhere, not one of the
            // SalesShortcuts table's own bindings — it works no matter what
            // else on the screen currently has focus. Closing it always works;
            // opening it waits for another open window the same way the rest
            // of the screen's shortcuts do (IsAnyWindowOpen already counts
            // Help itself, so this reduces to "is anything *else* open" here).
            if (HelpOverlay.Workflow is not null)
            {
                HelpOverlay.Workflow = null;
            }
            else if (!IsAnyWindowOpen)
            {
                HelpOverlay.Workflow = App.Workflows.Load("sales-workspace");
            }

            e.Handled = true;
            return;
        }

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
            // draft with items stays held (§6.13). Help is page-local state, not
            // one of the ViewModel's own dialog flags, so it is checked first —
            // otherwise CloseTopDialogCommand would see nothing open and do nothing.
            if (HelpOverlay.Workflow is not null)
            {
                HelpOverlay.Workflow = null;
            }
            else if (IsAnyWindowOpen)
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

    private void OnNoteLostFocus(object sender, RoutedEventArgs e) => ViewModel.CommitNoteCommand.Execute(null);

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

    private void OnReceivePaymentMethodClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag } && Enum.TryParse<CustomerPaymentMethod>(tag, out var method))
        {
            ViewModel.SelectReceivePaymentMethodCommand.Execute(method);
        }
    }

    private void OnAdminApprovalPasswordChanged(object sender, RoutedEventArgs e) =>
        ViewModel.AdminApprovalPassword = AdminApprovalPasswordBox.Password;

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

    private void OnReturnInvoiceClick(object sender, RoutedEventArgs e) =>
        RunWithItem<InvoiceListRow>(sender, ViewModel.OpenReturnForRowCommand);

    private void OnIncreaseReturnClick(object sender, RoutedEventArgs e) =>
        RunWithItem<ReturnLineRow>(sender, ViewModel.IncreaseReturnQuantityCommand);

    private void OnDecreaseReturnClick(object sender, RoutedEventArgs e) =>
        RunWithItem<ReturnLineRow>(sender, ViewModel.DecreaseReturnQuantityCommand);

    private void OnReturnRefundMethodClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag } && Enum.TryParse<PaymentMethod>(tag, out var method))
        {
            ViewModel.SelectReturnRefundMethodCommand.Execute(method);
        }
    }

    private void OnReturnNumberKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            ViewModel.FindReturnInvoiceCommand.Execute(null);
            e.Handled = true;
        }
    }

    // «دیدن فاکتور» → اصلاح / مرجوعی: the next window replaces this one, so close it first (سند و چاپ روی آن باز می‌شوند).
    private void OnViewCorrectClick(object sender, RoutedEventArgs e)
    {
        ViewModel.CloseInvoiceViewCommand.Execute(null);
        OnCorrectInvoiceClick(sender, e);
    }

    private void OnViewReturnClick(object sender, RoutedEventArgs e)
    {
        ViewModel.CloseInvoiceViewCommand.Execute(null);
        OnReturnInvoiceClick(sender, e);
    }

    private void OnCorrectInvoiceClick(object sender, RoutedEventArgs e) =>
        RunWithItem<InvoiceListRow>(sender, ViewModel.OpenCorrectionPromptCommand);

    private void OnJournalEntryClick(object sender, RoutedEventArgs e) =>
        RunWithItem<InvoiceListRow>(sender, ViewModel.OpenJournalEntryCommand);

    private void OnPrintReceiptClick(object sender, RoutedEventArgs e)
    {
        if (ItemOf<InvoiceListRow>(sender) is { } row)
        {
            ViewModel.OpenReceiptPreviewCommand.Execute(row.Item.SaleId);
        }
    }

    /// <summary>
    /// «چاپ» in the receipt preview — a real print job (§14.14: "چاپگر واقعی
    /// برای توسعه اجباری نیست؛ Print Preview و Adapter آزمایشی مسیر را
    /// پوشش می‌دهند"). Deliberately the plain GDI+ <see cref="PrintDocument"/>
    /// path rather than WinUI's own newer print surface: it needs no printer
    /// picker dialog to be real, always prints to Windows' current default
    /// printer (which can be "Microsoft Print to PDF" — no thermal printer
    /// hardware required to exercise this path), and does not depend on a
    /// WindowsAppSDK print API this project has not otherwise needed yet.
    /// </summary>
    private void OnActuallyPrintClick(object sender, RoutedEventArgs e)
    {
        var lines = BuildReceiptPrintLines();
        if (lines.Count == 0)
        {
            return;
        }

        try
        {
            using var printDocument = new System.Drawing.Printing.PrintDocument();
            var nextLine = 0;
            printDocument.PrintPage += (_, args) =>
            {
                using var font = new System.Drawing.Font("Consolas", 10);
                var graphics = args.Graphics!;
                var lineHeight = font.GetHeight(graphics);
                var y = (float)args.MarginBounds.Top;
                while (nextLine < lines.Count && y + lineHeight <= args.MarginBounds.Bottom)
                {
                    graphics.DrawString(lines[nextLine], font, System.Drawing.Brushes.Black, args.MarginBounds.Left, y);
                    y += lineHeight;
                    nextLine++;
                }

                args.HasMorePages = nextLine < lines.Count;
            };

            printDocument.Print();
            ViewModel.ShowPrintedNotice();
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // No default printer configured, or the spooler refused — a plain
            // Persian message, not a stack trace (§15.20).
            ViewModel.ShowPrintFailedNotice();
        }
    }

    private List<string> BuildReceiptPrintLines()
    {
        var lines = new List<string>
        {
            "پیشکار ERP",
            $"فاکتور {ViewModel.ReceiptNumberText}",
            ViewModel.ReceiptDateText,
            ViewModel.ReceiptCustomerText,
            new string('-', 32),
        };

        foreach (var line in ViewModel.ReceiptLines)
        {
            lines.Add(line.Name);
            lines.Add($"  {line.QuantityAndPrice}    {line.TotalText}");
        }

        lines.Add(new string('-', 32));
        lines.Add($"جمع کالاها: {ViewModel.ReceiptSubtotalText}");
        if (ViewModel.HasReceiptDiscount)
        {
            lines.Add($"تخفیف: {ViewModel.ReceiptDiscountText}");
        }

        if (ViewModel.HasReceiptServiceCharge)
        {
            lines.Add($"هزینه/خدمات: {ViewModel.ReceiptServiceChargeText}");
        }

        if (ViewModel.HasReceiptTax)
        {
            lines.Add($"مالیات: {ViewModel.ReceiptTaxText}");
        }

        lines.Add($"قابل پرداخت: {ViewModel.ReceiptTotalText}");
        lines.Add($"روش پرداخت: {ViewModel.ReceiptPaymentMethodText}");
        if (ViewModel.HasReceiptNote)
        {
            lines.Add(ViewModel.ReceiptNoteText!);
        }

        return lines;
    }
}
