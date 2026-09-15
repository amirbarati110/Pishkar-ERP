using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ERP.Application.Catalog;
using ERP.Application.Sales;
using ERP.Domain.Catalog;
using ERP.Domain.Inventory;
using ERP.Presentation.Common;

namespace ERP.Presentation.Features.Sales;

public sealed record CartLineDisplay(
    ProductId ProductId,
    string Name,
    string QuantityDisplay,
    string UnitPriceDisplay,
    string LineTotalDisplay);

/// <summary>
/// Drives the "فروش سریع" (Quick Sale) cashier screen — the single
/// highest-frequency screen in the app. Search is debounced (not fired on
/// every keystroke) but still meant to feel instant: see source-of-truth
/// Appendix B on why sub-second product search is a hard requirement, not a
/// nice-to-have, for this specific screen.
/// </summary>
public partial class QuickSaleViewModel : ObservableObject
{
    /// <summary>
    /// Iran VAT. Hardcoded here deliberately, not buried deeper: it belongs to
    /// Phase 4 Tax Infrastructure (source-of-truth §10.10), which does not
    /// exist yet. When it lands, this constant is the one place to replace
    /// with a real setting.
    /// </summary>
    private const decimal DefaultTaxRatePercent = 9m;

    private static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(200);

    private readonly IStartSaleHandler _startSale;
    private readonly IAddSaleLineHandler _addLine;
    private readonly IRemoveSaleLineHandler _removeLine;
    private readonly ICompleteSaleHandler _completeSale;
    private readonly IGetSaleSummaryHandler _getSummary;
    private readonly ISearchProductsHandler _search;
    private readonly WarehouseId _warehouseId;
    private readonly Dictionary<Guid, string> _productNames = [];

    private CancellationTokenSource? _searchDebounceSource;
    private Domain.Sales.SaleId? _saleId;

    public QuickSaleViewModel(
        IStartSaleHandler startSale,
        IAddSaleLineHandler addLine,
        IRemoveSaleLineHandler removeLine,
        ICompleteSaleHandler completeSale,
        IGetSaleSummaryHandler getSummary,
        ISearchProductsHandler search,
        WarehouseId warehouseId)
    {
        _startSale = startSale;
        _addLine = addLine;
        _removeLine = removeLine;
        _completeSale = completeSale;
        _getSummary = getSummary;
        _search = search;
        _warehouseId = warehouseId;
    }

    public ObservableCollection<ProductSearchResult> SearchResults { get; } = [];

    public ObservableCollection<CartLineDisplay> CartLines { get; } = [];

    public IReadOnlyList<Domain.Sales.PaymentMethod> PaymentMethods { get; } =
        [Domain.Sales.PaymentMethod.Cash, Domain.Sales.PaymentMethod.Card];

    [ObservableProperty]
    public partial string SearchTerm { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsSearching { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    [ObservableProperty]
    public partial string DiscountInput { get; set; } = string.Empty;

    /// <summary>
    /// Off by default: completing over recorded stock is a deliberate,
    /// visible choice the cashier/manager makes per sale, not a silent
    /// default (source-of-truth §3.14; Appendix B follow-up on backorder sales).
    /// </summary>
    [ObservableProperty]
    public partial bool AllowNegativeStock { get; set; }

    [ObservableProperty]
    public partial Domain.Sales.PaymentMethod SelectedPaymentMethod { get; set; } = Domain.Sales.PaymentMethod.Cash;

    [ObservableProperty]
    public partial string SubtotalDisplay { get; set; } = FormatToman(0);

    [ObservableProperty]
    public partial string TaxDisplay { get; set; } = FormatToman(0);

    [ObservableProperty]
    public partial string TotalDisplay { get; set; } = FormatToman(0);

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        ErrorMessage = null;
        var result = await _startSale
            .ExecuteAsync(new StartSaleCommand(_warehouseId, CustomerId: null), cancellationToken)
            .ConfigureAwait(true);

        if (result.IsSuccess)
        {
            _saleId = result.Value;
            await RefreshCartAsync(cancellationToken).ConfigureAwait(true);
        }
        else
        {
            ErrorMessage = result.Error?.Message;
        }
    }

    partial void OnSearchTermChanged(string value)
    {
        _searchDebounceSource?.Cancel();
        var debounceSource = new CancellationTokenSource();
        _searchDebounceSource = debounceSource;
        _ = RunDebouncedSearchAsync(value, debounceSource.Token);
    }

    [RelayCommand]
    private async Task AddToCartAsync(ProductSearchResult product)
    {
        if (_saleId is not { } saleId || product is null)
        {
            return;
        }

        ErrorMessage = null;
        _productNames[product.Id.Value] = product.Name;

        var result = await _addLine
            .ExecuteAsync(new AddSaleLineCommand(saleId, product.Id, 1), CancellationToken.None)
            .ConfigureAwait(true);

        if (result.IsSuccess)
        {
            SearchTerm = string.Empty;
            SearchResults.Clear();
            await RefreshCartAsync(CancellationToken.None).ConfigureAwait(true);
        }
        else
        {
            ErrorMessage = result.Error?.Message;
        }
    }

    [RelayCommand]
    private async Task RemoveFromCartAsync(ProductId productId)
    {
        if (_saleId is not { } saleId)
        {
            return;
        }

        ErrorMessage = null;
        var result = await _removeLine
            .ExecuteAsync(new RemoveSaleLineCommand(saleId, productId), CancellationToken.None)
            .ConfigureAwait(true);

        if (result.IsSuccess)
        {
            await RefreshCartAsync(CancellationToken.None).ConfigureAwait(true);
        }
        else
        {
            ErrorMessage = result.Error?.Message;
        }
    }

    [RelayCommand]
    private async Task CompleteSaleAsync()
    {
        if (_saleId is not { } saleId)
        {
            return;
        }

        ErrorMessage = null;
        StatusMessage = null;
        IsBusy = true;

        try
        {
            var discountTomans = PersianInputParser.TryParseInt64(DiscountInput, out var parsedDiscount)
                ? parsedDiscount
                : 0;

            var result = await _completeSale.ExecuteAsync(
                new CompleteSaleCommand(
                    saleId,
                    SelectedPaymentMethod,
                    discountTomans * 10,
                    DefaultTaxRatePercent,
                    AllowNegativeStock),
                CancellationToken.None).ConfigureAwait(true);

            if (result.IsSuccess)
            {
                var completed = result.Value!;
                StatusMessage = $"فاکتور {completed.Number.ToPersianString()} با موفقیت ثبت شد — مبلغ نهایی {FormatToman(completed.Totals.Total.ToTomansExact())} تومان";
                await StartNewSaleAsync().ConfigureAwait(true);
            }
            else
            {
                ErrorMessage = result.Error?.Message;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task StartNewSaleAsync()
    {
        CartLines.Clear();
        SearchResults.Clear();
        SearchTerm = string.Empty;
        DiscountInput = string.Empty;
        AllowNegativeStock = false;
        SelectedPaymentMethod = Domain.Sales.PaymentMethod.Cash;
        await InitializeAsync(CancellationToken.None).ConfigureAwait(true);
    }

    private async Task RunDebouncedSearchAsync(string term, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(SearchDebounce, cancellationToken).ConfigureAwait(true);

            if (string.IsNullOrWhiteSpace(term))
            {
                SearchResults.Clear();
                return;
            }

            IsSearching = true;
            var results = await _search
                .ExecuteAsync(new SearchProductsQuery(term), cancellationToken)
                .ConfigureAwait(true);

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            foreach (var product in results)
            {
                _productNames[product.Id.Value] = product.Name;
            }

            SearchResults.Clear();
            foreach (var product in results)
            {
                SearchResults.Add(product);
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer keystroke — not an error.
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                IsSearching = false;
            }
        }
    }

    private async Task RefreshCartAsync(CancellationToken cancellationToken)
    {
        if (_saleId is not { } saleId)
        {
            return;
        }

        var result = await _getSummary
            .ExecuteAsync(new GetSaleSummaryQuery(saleId), cancellationToken)
            .ConfigureAwait(true);

        if (!result.IsSuccess || result.Value is null)
        {
            return;
        }

        var summary = result.Value;

        CartLines.Clear();
        foreach (var line in summary.Lines)
        {
            CartLines.Add(new CartLineDisplay(
                line.ProductId,
                _productNames.GetValueOrDefault(line.ProductId.Value, "کالا"),
                line.Quantity.ToString("0.###", CultureInfo.InvariantCulture),
                FormatToman(line.UnitPrice.ToTomansExact()),
                FormatToman(line.LineTotal.ToTomansExact())));
        }

        var discountTomans = PersianInputParser.TryParseInt64(DiscountInput, out var parsedDiscount)
            ? parsedDiscount
            : 0;
        var subtotalTomans = summary.Subtotal.ToTomansExact();
        var discountApplied = Math.Clamp(discountTomans, 0, subtotalTomans);
        var taxableTomans = subtotalTomans - discountApplied;
        var taxTomans = (long)Math.Round(
            taxableTomans * DefaultTaxRatePercent / 100m,
            0,
            MidpointRounding.AwayFromZero);

        SubtotalDisplay = FormatToman(subtotalTomans);
        TaxDisplay = FormatToman(taxTomans);
        TotalDisplay = FormatToman(taxableTomans + taxTomans);
    }

    private static string FormatToman(long amountTomans) => amountTomans.ToString("#,0", CultureInfo.InvariantCulture);
}
