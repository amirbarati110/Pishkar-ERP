using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ERP.Application.Customers;
using ERP.Application.Sales;
using ERP.Domain.Common;

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
