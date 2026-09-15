using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ERP.Application.Catalog;
using ERP.Application.Common;
using ERP.Application.Sales;
using ERP.Domain.Catalog;
using ERP.Domain.Inventory;

namespace ERP.Presentation.Features.Sales;

/// <summary>
/// The sales workspace — the approved «فروش سریع» screen: invoice tabs, the
/// product list and category tiles, the invoice with its footer, and the
/// payment / edit / list windows (partial files).
///
/// Rules this class keeps:
/// <list type="bullet">
/// <item>It never computes money. Every amount on screen comes back from the
/// server after each change (<see cref="RefreshInvoiceAsync"/>), including the
/// payable total at the tax rate on screen, so what is shown is what completion
/// charges.</item>
/// <item>One action at a time (<see cref="IsBusy"/>): a double press of F6 cannot
/// complete an invoice twice.</item>
/// <item>Failures are shown as plain Persian (<see cref="Notice"/>), never thrown
/// at the cashier.</item>
/// </list>
/// </summary>
public sealed partial class SalesWorkspaceViewModel : ObservableObject
{
    public const int ProductPageSize = 15;
    public const int TileCount = 12;
    private const int SearchResultLimit = 50;
    private static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(200);

    private readonly SalesBackend _backend;
    private readonly WarehouseId _warehouseId;
    private CancellationTokenSource? _searchDebounce;

    public SalesWorkspaceViewModel(SalesBackend backend, WarehouseId warehouseId)
    {
        ArgumentNullException.ThrowIfNull(backend);

        _backend = backend;
        _warehouseId = warehouseId;
    }

    public ObservableCollection<InvoiceTab> Tabs { get; } = [];

    public ObservableCollection<CategoryChip> Categories { get; } = [];

    public ObservableCollection<ProductRow> Tiles { get; } = [];

    public ObservableCollection<ProductRow> Products { get; } = [];

    [ObservableProperty]
    public partial InvoiceTab? CurrentTab { get; set; }

    [ObservableProperty]
    public partial ProductListFilter ProductFilter { get; set; } = ProductListFilter.All;

    [ObservableProperty]
    public partial int ProductPage { get; set; } = 1;

    [ObservableProperty]
    public partial int ProductPageCount { get; set; } = 1;

    [ObservableProperty]
    public partial string ProductSearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsSearchingProducts { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>The short message the screen shows as a toast; <see cref="NoticeIsError"/> colours it.</summary>
    [ObservableProperty]
    public partial string? Notice { get; set; }

    [ObservableProperty]
    public partial bool NoticeIsError { get; set; }

    /// <summary>«۲ از ۳» for the open-invoices pager in the invoice header.</summary>
    public string TabPositionText => CurrentTab is null
        ? string.Empty
        : $"{Domain.Common.PersianNumber.FormatGrouped(Tabs.IndexOf(CurrentTab) + 1)} از {Domain.Common.PersianNumber.FormatGrouped(Tabs.Count)}";

    public string ProductPageText =>
        $"{Domain.Common.PersianNumber.FormatGrouped(ProductPage)} از {Domain.Common.PersianNumber.FormatGrouped(ProductPageCount)}";

    public bool IsBrowsing => ProductSearchText.Trim().Length == 0;

    private CategoryId? SelectedCategoryId => Categories.FirstOrDefault(chip => chip.IsSelected)?.Id;

    /// <summary>
    /// Opens the workspace: every held invoice comes back as a tab (so nothing
    /// a customer was promised is lost when the program closed), or a fresh one
    /// is started.
    /// </summary>
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        await RunAsync(async () =>
        {
            var catalog = await _backend.CatalogLookup.LoadAsync(cancellationToken);
            Categories.Clear();
            Categories.Add(new CategoryChip(null, "همه") { IsSelected = true });
            foreach (var category in catalog.Categories.Where(item => item.ParentId is null))
            {
                Categories.Add(new CategoryChip(category.Id, category.Name));
            }

            Tabs.Clear();
            foreach (var held in await _backend.HeldSales.ExecuteAsync(new ListHeldSalesQuery(), cancellationToken))
            {
                Tabs.Add(new InvoiceTab(held.SaleId));
            }

            if (Tabs.Count == 0)
            {
                await AddNewTabCoreAsync(cancellationToken);
            }

            foreach (var tab in Tabs)
            {
                await RefreshInvoiceAsync(tab, cancellationToken);
            }

            CurrentTab = Tabs[0];
            await LoadProductsAsync(cancellationToken);
            await LoadTilesAsync(cancellationToken);
        });
    }

    partial void OnCurrentTabChanged(InvoiceTab? value) => OnPropertyChanged(nameof(TabPositionText));

    partial void OnProductPageChanged(int value) => OnPropertyChanged(nameof(ProductPageText));

    partial void OnProductPageCountChanged(int value) => OnPropertyChanged(nameof(ProductPageText));

    // ───── tabs ─────

    [RelayCommand]
    private Task NewInvoiceAsync() => RunAsync(async () =>
    {
        await AddNewTabCoreAsync(CancellationToken.None);
        CurrentTab = Tabs[^1];
        OnPropertyChanged(nameof(TabPositionText));
    });

    [RelayCommand]
    private void SelectTab(InvoiceTab tab) => CurrentTab = tab;

    [RelayCommand]
    private void PreviousTab() => MoveTab(-1);

    [RelayCommand]
    private void NextTab() => MoveTab(1);

    /// <summary>
    /// Closing a tab holds the invoice: a draft with items stays in «فاکتورهای
    /// معلق» and can be reopened from the invoice list. An empty tab simply goes.
    /// The last tab is never left closed — the screen always has an invoice.
    /// </summary>
    [RelayCommand]
    private Task CloseTabAsync(InvoiceTab tab) => RunAsync(async () =>
    {
        var wasHeld = !tab.IsEmpty;
        var index = Tabs.IndexOf(tab);
        Tabs.Remove(tab);

        if (Tabs.Count == 0)
        {
            await AddNewTabCoreAsync(CancellationToken.None);
        }

        if (CurrentTab == tab || CurrentTab is null)
        {
            CurrentTab = Tabs[Math.Clamp(index, 0, Tabs.Count - 1)];
        }

        OnPropertyChanged(nameof(TabPositionText));
        if (wasHeld)
        {
            ShowNotice("فاکتور معلق شد؛ از «لیست فاکتورها» دوباره باز می‌شود.");
        }
    });

    private void MoveTab(int step)
    {
        if (CurrentTab is null || Tabs.Count < 2)
        {
            return;
        }

        var index = (Tabs.IndexOf(CurrentTab) + step + Tabs.Count) % Tabs.Count;
        CurrentTab = Tabs[index];
    }

    private async Task AddNewTabCoreAsync(CancellationToken cancellationToken)
    {
        var started = await _backend.StartSale.ExecuteAsync(new StartSaleCommand(_warehouseId), cancellationToken);
        if (!started.IsSuccess)
        {
            throw new SalesScreenException(started.Error?.Message ?? "فاکتور جدید باز نشد.");
        }

        Tabs.Add(new InvoiceTab(started.Value));
    }

    // ───── product list and tiles ─────

    [RelayCommand]
    private Task SelectCategoryAsync(CategoryChip chip) => RunAsync(async () =>
    {
        foreach (var item in Categories)
        {
            item.IsSelected = item == chip;
        }

        ProductPage = 1;
        await LoadProductsAsync(CancellationToken.None);
        await LoadTilesAsync(CancellationToken.None);
    });

    [RelayCommand]
    private Task SetProductFilterAsync(ProductListFilter filter) => RunAsync(async () =>
    {
        ProductFilter = filter;
        ProductPage = 1;
        await LoadProductsAsync(CancellationToken.None);
    });

    [RelayCommand]
    private Task ChangeProductPageAsync(int step) => RunAsync(async () =>
    {
        var target = Math.Clamp(ProductPage + step, 1, ProductPageCount);
        if (target == ProductPage)
        {
            return;
        }

        ProductPage = target;
        await LoadProductsAsync(CancellationToken.None);
    });

    partial void OnProductSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(IsBrowsing));
        _searchDebounce?.Cancel();
        _searchDebounce?.Dispose();
        _searchDebounce = new CancellationTokenSource();
        _ = SearchAfterPauseAsync(value, _searchDebounce.Token);
    }

    /// <summary>
    /// Enter in the search box — also what a barcode scanner sends at the end
    /// of a scan (§6.8). An exact barcode/SKU adds that product; otherwise a
    /// search with a single result adds it; anything else leaves the list for
    /// the cashier to pick from.
    /// </summary>
    [RelayCommand]
    private Task SubmitProductSearchAsync() => RunAsync(async () =>
    {
        var term = ProductSearchText.Trim();
        if (term.Length == 0)
        {
            return;
        }

        var exact = await _backend.SearchProducts.FindByExactCodeAsync(term, CancellationToken.None);
        var productId = exact?.Id;

        if (productId is null)
        {
            var matches = await _backend.SearchProducts.ExecuteAsync(new SearchProductsQuery(term, 2), CancellationToken.None);
            if (matches.Count == 1)
            {
                productId = matches[0].Id;
            }
        }

        if (productId is null)
        {
            ShowNotice($"کالایی دقیقاً با «{term}» پیدا نشد؛ از لیست انتخاب کنید.", isError: true);
            return;
        }

        await AddProductCoreAsync(productId.Value);
        ProductSearchText = string.Empty;
    });

    private async Task SearchAfterPauseAsync(string term, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(SearchDebounce, cancellationToken);
            await LoadProductsAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Superseded by the next keystroke.
        }
    }

    private async Task LoadProductsAsync(CancellationToken cancellationToken)
    {
        var term = ProductSearchText.Trim();
        if (term.Length > 0)
        {
            IsSearchingProducts = true;
            try
            {
                var results = await _backend.SearchProducts.ExecuteAsync(
                    new SearchProductsQuery(term, SearchResultLimit), cancellationToken);
                var info = await _backend.ReadProducts.ExecuteAsync(
                    new ReadSaleProductsQuery(_warehouseId, results.Select(result => result.Id).ToList()), cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                Products.Clear();
                foreach (var result in results)
                {
                    var product = info.GetValueOrDefault(result.Id);
                    Products.Add(new ProductRow(
                        result.Id, result.Name, result.Sku, product?.UnitSymbol ?? string.Empty, result.SalePrice, product?.Available));
                }

                ProductPage = 1;
                ProductPageCount = 1;
            }
            finally
            {
                IsSearchingProducts = false;
            }

            return;
        }

        var page = await _backend.BrowseProducts.ExecuteAsync(
            new BrowseProductsForSaleQuery(_warehouseId, SelectedCategoryId, ProductFilter, ProductPage, ProductPageSize),
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        Products.Clear();
        foreach (var item in page.Items)
        {
            Products.Add(new ProductRow(item.Id, item.Name, item.Sku, item.UnitSymbol, item.SalePrice, item.Available));
        }

        ProductPageCount = page.PageCount;
    }

    /// <summary>Tiles are the quick-access grid: what sells most in the chosen category, or its first products if nothing has sold yet.</summary>
    private async Task LoadTilesAsync(CancellationToken cancellationToken)
    {
        var page = await _backend.BrowseProducts.ExecuteAsync(
            new BrowseProductsForSaleQuery(_warehouseId, SelectedCategoryId, ProductListFilter.TopSelling, 1, TileCount),
            cancellationToken);
        if (page.Items.Count == 0)
        {
            page = await _backend.BrowseProducts.ExecuteAsync(
                new BrowseProductsForSaleQuery(_warehouseId, SelectedCategoryId, ProductListFilter.All, 1, TileCount),
                cancellationToken);
        }

        Tiles.Clear();
        foreach (var item in page.Items)
        {
            Tiles.Add(new ProductRow(item.Id, item.Name, item.Sku, item.UnitSymbol, item.SalePrice, item.Available));
        }
    }

    // ───── cart ─────

    [RelayCommand]
    private Task AddProductAsync(ProductRow product) => RunAsync(() => AddProductCoreAsync(product.Id));

    [RelayCommand]
    private Task IncreaseLineAsync(CartLineRow row) => RunAsync(() => ChangeQuantityCoreAsync(row, row.Line.Quantity + 1));

    [RelayCommand]
    private Task DecreaseLineAsync(CartLineRow row) => RunAsync(() => ChangeQuantityCoreAsync(row, row.Line.Quantity - 1));

    [RelayCommand]
    private Task RemoveLineAsync(CartLineRow row) => RunAsync(async () =>
    {
        var tab = RequireTab();
        Check(await _backend.RemoveLine.ExecuteAsync(new RemoveSaleLineCommand(tab.SaleId, row.ProductId), CancellationToken.None));
        await RefreshInvoiceAsync(tab, CancellationToken.None);
    });

    /// <summary>
    /// Saves the footer the cashier typed: discount as an amount or a percent
    /// (§6.10), and the service charge. Called when a footer box loses focus.
    /// A percent is turned into an amount of the current goods subtotal here,
    /// because the invoice stores money, not ratios.
    /// </summary>
    [RelayCommand]
    private Task CommitChargesAsync(string? changedField) => RunAsync(async () =>
    {
        var tab = RequireTab();

        long? discountRials = changedField == "discount-percent"
            ? SalesText.ParseDecimal(tab.DiscountPercentInput) is { } percent
                ? tab.Subtotal.Multiply(Math.Min(percent, 100m) / 100m).Rials
                : null
            : SalesText.ParseTomansToRials(tab.DiscountInput);
        var serviceRials = SalesText.ParseTomansToRials(tab.ServiceChargeInput);

        if (discountRials is null || serviceRials is null)
        {
            ShowNotice("مبلغ را فقط با عدد وارد کنید.", isError: true);
            await RefreshInvoiceAsync(tab, CancellationToken.None);
            return;
        }

        var result = await _backend.SetCharges.ExecuteAsync(
            new SetSaleChargesCommand(tab.SaleId, discountRials.Value, serviceRials.Value), CancellationToken.None);
        if (!result.IsSuccess)
        {
            ShowNotice(result.Error?.Message ?? "تخفیف ثبت نشد.", isError: true);
        }

        await RefreshInvoiceAsync(tab, CancellationToken.None);
    });

    /// <summary>
    /// The tax rate on screen (§10.10). It belongs to this invoice until it is
    /// completed, so it is not saved on the draft: a held invoice reopens at
    /// the default rate.
    /// </summary>
    [RelayCommand]
    private Task CommitTaxRateAsync() => RunAsync(async () =>
    {
        var tab = RequireTab();
        if (SalesText.ParseDecimal(tab.TaxPercentInput) is { } rate && rate <= 100)
        {
            tab.TaxRatePercent = rate;
        }
        else
        {
            ShowNotice("درصد مالیات باید عددی بین ۰ و ۱۰۰ باشد.", isError: true);
        }

        tab.TaxPercentInput = SalesText.Percent(tab.TaxRatePercent);
        await RefreshInvoiceAsync(tab, CancellationToken.None);
    });

    private async Task AddProductCoreAsync(ProductId productId)
    {
        var tab = RequireTab();
        Check(await _backend.AddLine.ExecuteAsync(new AddSaleLineCommand(tab.SaleId, productId, 1), CancellationToken.None));
        await RefreshInvoiceAsync(tab, CancellationToken.None);
    }

    private async Task ChangeQuantityCoreAsync(CartLineRow row, decimal quantity)
    {
        var tab = RequireTab();
        if (quantity <= 0)
        {
            Check(await _backend.RemoveLine.ExecuteAsync(new RemoveSaleLineCommand(tab.SaleId, row.ProductId), CancellationToken.None));
        }
        else
        {
            Check(await _backend.ChangeLine.ExecuteAsync(
                new ChangeSaleLineCommand(tab.SaleId, row.ProductId, quantity, row.Line.UnitPrice.Rials, row.Line.Discount.Rials),
                CancellationToken.None));
        }

        await RefreshInvoiceAsync(tab, CancellationToken.None);
    }

    // ───── plumbing ─────

    /// <summary>Reloads one invoice from the server, including its customer's account banner.</summary>
    private async Task RefreshInvoiceAsync(InvoiceTab tab, CancellationToken cancellationToken)
    {
        var details = await _backend.Details.ExecuteAsync(
            new GetSaleDetailsQuery(tab.SaleId, tab.TaxRatePercent), cancellationToken);
        if (!details.IsSuccess || details.Value is null)
        {
            throw new SalesScreenException(details.Error?.Message ?? "فاکتور خوانده نشد.");
        }

        tab.Apply(details.Value);

        if (details.Value.CustomerId is { } customerId)
        {
            var account = await _backend.CustomerAccount.ExecuteAsync(
                new Application.Customers.GetCustomerAccountQuery(customerId), cancellationToken);
            if (account.IsSuccess && account.Value is not null)
            {
                tab.ApplyAccount(account.Value);
            }
        }
    }

    private InvoiceTab RequireTab() => CurrentTab ?? throw new SalesScreenException("فاکتوری باز نیست.");

    private static void Check(Result<bool> result)
    {
        if (!result.IsSuccess)
        {
            throw new SalesScreenException(result.Error?.Message ?? "عملیات انجام نشد.");
        }
    }

    private void ShowNotice(string message, bool isError = false)
    {
        NoticeIsError = isError;
        Notice = null; // re-raise even when the same text repeats
        Notice = message;
    }

    /// <summary>
    /// Runs one cashier action: refuses to start while another is running, and
    /// turns an expected failure (<see cref="SalesScreenException"/>) into a
    /// notice. Anything else is a bug and is left to the global crash handler.
    /// </summary>
    private async Task RunAsync(Func<Task> action)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await action();
        }
        catch (SalesScreenException exception)
        {
            ShowNotice(exception.Message, isError: true);
        }
        finally
        {
            IsBusy = false;
        }
    }
}

/// <summary>An expected, explainable failure of a cashier action — its message is shown as is.</summary>
public sealed class SalesScreenException : Exception
{
    public SalesScreenException(string message)
        : base(message)
    {
    }

    public SalesScreenException()
    {
    }

    public SalesScreenException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
