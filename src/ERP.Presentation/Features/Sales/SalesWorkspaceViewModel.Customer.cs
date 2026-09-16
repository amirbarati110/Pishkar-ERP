using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ERP.Application.Customers;
using ERP.Application.Sales;
using ERP.Domain.Common;
using ERP.Domain.Customers;

namespace ERP.Presentation.Features.Sales;

/// <summary>Choosing and registering the invoice's customer (§6.3–§6.6).</summary>
public sealed partial class SalesWorkspaceViewModel
{
    private CancellationTokenSource? _customerDebounce;

    public ObservableCollection<CustomerSearchResult> CustomerSuggestions { get; } = [];

    [ObservableProperty]
    public partial string CustomerSearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsNewCustomerOpen { get; set; }

    [ObservableProperty]
    public partial string NewCustomerName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NewCustomerMobile { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? NewCustomerError { get; set; }

    partial void OnCustomerSearchTextChanged(string value)
    {
        _customerDebounce?.Cancel();
        _customerDebounce?.Dispose();
        _customerDebounce = new CancellationTokenSource();
        _ = SearchCustomersAfterPauseAsync(value, _customerDebounce.Token);
    }

    [RelayCommand]
    private Task ChooseCustomerAsync(CustomerSearchResult? customer) => customer is null ? Task.CompletedTask : RunAsync(async () =>
    {
        var tab = RequireTab();
        Check(await _backend.SetCustomer.ExecuteAsync(new SetSaleCustomerCommand(tab.SaleId, customer.Id), CancellationToken.None));
        CustomerSearchText = string.Empty;
        CustomerSuggestions.Clear();
        await RefreshInvoiceAsync(tab, CancellationToken.None);
    });

    /// <summary>Back to «مشتری نقدی».</summary>
    [RelayCommand]
    private Task ClearCustomerAsync() => RunAsync(async () =>
    {
        var tab = RequireTab();
        Check(await _backend.SetCustomer.ExecuteAsync(new SetSaleCustomerCommand(tab.SaleId, null), CancellationToken.None));
        await RefreshInvoiceAsync(tab, CancellationToken.None);
    });

    /// <summary>
    /// «+ مشتری جدید». Whatever was typed in the customer box is carried over —
    /// digits into the mobile field, anything else into the name — so the
    /// cashier does not type it twice.
    /// </summary>
    [RelayCommand]
    private void OpenNewCustomer()
    {
        var typed = CustomerSearchText.Trim();
        var looksLikeMobile = typed.Any(char.IsDigit) && typed.All(character => char.IsDigit(character) || character is ' ' or '-');
        NewCustomerName = looksLikeMobile ? string.Empty : typed;
        NewCustomerMobile = looksLikeMobile ? typed : string.Empty;
        NewCustomerError = null;
        IsNewCustomerOpen = true;
    }

    [RelayCommand]
    private void CancelNewCustomer() => IsNewCustomerOpen = false;

    [RelayCommand]
    private Task SaveNewCustomerAsync() => RunAsync(async () =>
    {
        var tab = RequireTab();
        var created = await _backend.QuickCreateCustomer.ExecuteAsync(
            new QuickCreateCustomerCommand(NewCustomerName, NewCustomerMobile), CancellationToken.None);
        if (!created.IsSuccess)
        {
            NewCustomerError = created.Error?.Message ?? "مشتری ثبت نشد.";
            return;
        }

        Check(await _backend.SetCustomer.ExecuteAsync(new SetSaleCustomerCommand(tab.SaleId, created.Value), CancellationToken.None));
        IsNewCustomerOpen = false;
        CustomerSearchText = string.Empty;
        await RefreshInvoiceAsync(tab, CancellationToken.None);
        ShowNotice("مشتری ثبت و روی فاکتور انتخاب شد.");
    });

    // ───── دریافت از مشتری (§7.1) ─────

    [ObservableProperty]
    public partial bool IsReceivePaymentOpen { get; set; }

    [ObservableProperty]
    public partial string ReceivePaymentCustomerName { get; set; } = string.Empty;

    /// <summary>«بدهی فعلی: … تومان» / «بستانکار: … تومان» / «بدون بدهی» — read fresh each time the window opens, not carried over from the balance banner.</summary>
    [ObservableProperty]
    public partial string ReceivePaymentAccountText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ReceivePaymentAmountText { get; set; } = "۰";

    [ObservableProperty]
    public partial CustomerPaymentMethod ReceivePaymentMethod { get; set; } = CustomerPaymentMethod.Cash;

    [ObservableProperty]
    public partial string ReceivePaymentNote { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? ReceivePaymentError { get; set; }

    public IReadOnlyList<CustomerPaymentMethod> ReceivePaymentMethods { get; } =
        [CustomerPaymentMethod.Cash, CustomerPaymentMethod.Card];

    /// <summary>
    /// Opens the window with the customer's balance read fresh from the
    /// server — the invoice's own banner can be a step behind (it was last
    /// refreshed when the invoice itself last changed), and a cashier about
    /// to record money must see the true amount owed right now.
    /// </summary>
    [RelayCommand]
    private Task OpenReceivePaymentAsync() => RunAsync(async () =>
    {
        var tab = RequireTab();
        if (tab.CustomerId is not { } customerId)
        {
            return; // guarded by IsEnabled in the XAML too — «مشتری نقدی» has no account to receive against
        }

        var account = await _backend.CustomerAccount.ExecuteAsync(
            new GetCustomerAccountQuery(customerId), CancellationToken.None);
        if (!account.IsSuccess || account.Value is null)
        {
            throw new SalesScreenException(account.Error?.Message ?? "اطلاعات مشتری خوانده نشد.");
        }

        ReceivePaymentCustomerName = account.Value.Name;
        ReceivePaymentAccountText = account.Value.Debt.Rials > 0
            ? $"بدهی فعلی: {SalesText.Tomans(account.Value.Debt)} تومان"
            : account.Value.Advance.Rials > 0
                ? $"بستانکار: {SalesText.Tomans(account.Value.Advance)} تومان"
                : "بدون بدهی";
        // A sensible starting point, not a lock — the cashier types what was actually handed over.
        ReceivePaymentAmountText = account.Value.Debt.Rials > 0 ? SalesText.Tomans(account.Value.Debt) : "۰";
        ReceivePaymentMethod = CustomerPaymentMethod.Cash;
        ReceivePaymentNote = string.Empty;
        ReceivePaymentError = null;
        IsReceivePaymentOpen = true;
    });

    [RelayCommand]
    private void SelectReceivePaymentMethod(CustomerPaymentMethod method) => ReceivePaymentMethod = method;

    [RelayCommand]
    private void CancelReceivePayment() => IsReceivePaymentOpen = false;

    [RelayCommand]
    private Task ConfirmReceivePaymentAsync() => RunAsync(async () =>
    {
        var tab = RequireTab();
        if (tab.CustomerId is not { } customerId)
        {
            return;
        }

        var amountRials = SalesText.ParseTomansToRials(ReceivePaymentAmountText);
        if (amountRials is not > 0)
        {
            ReceivePaymentError = "مبلغ باید بیشتر از صفر باشد.";
            return;
        }

        var note = string.IsNullOrWhiteSpace(ReceivePaymentNote) ? null : ReceivePaymentNote.Trim();
        var result = await _backend.ReceivePayment.ExecuteAsync(
            new RecordCustomerPaymentCommand(customerId, amountRials.Value, ReceivePaymentMethod, note),
            CancellationToken.None);
        if (!result.IsSuccess)
        {
            ReceivePaymentError = result.Error?.Message ?? "دریافت ثبت نشد.";
            return;
        }

        IsReceivePaymentOpen = false;
        await RefreshInvoiceAsync(tab, CancellationToken.None);
        ShowNotice($"دریافت {SalesText.Tomans(amountRials.Value)} تومان از {ReceivePaymentCustomerName} ثبت شد.");
    });

    private async Task SearchCustomersAfterPauseAsync(string term, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(SearchDebounce, cancellationToken);
            var results = await _backend.SearchCustomers.ExecuteAsync(new SearchCustomersQuery(term), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            CustomerSuggestions.Clear();
            foreach (var result in results)
            {
                CustomerSuggestions.Add(result);
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded by the next keystroke.
        }
    }
}
