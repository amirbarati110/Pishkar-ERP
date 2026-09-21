using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ERP.Application.Catalog;
using ERP.Domain.Catalog;
using ERP.Domain.Common;
using ERP.Presentation.Features.Sales;

namespace ERP.Presentation.Features.Products;

/// <summary>One row of «لیست کالاها» as the screen writes it (Persian digits, Tomans).</summary>
public sealed class ProductListItem
{
    public ProductListItem(ProductListRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        Row = row;
        Name = row.Name;
        CodeText = row.Sku is { } sku ? PersianNumber.DigitsToPersian(sku) : string.Empty;
        CategoryName = row.CategoryName;
        UnitSymbol = row.UnitSymbol;
        PriceText = SalesText.Tomans(row.SalePrice);
        StockText = SalesText.Quantity(row.Stock);
        IsOutOfStock = row.Stock <= 0;
    }

    public ProductListRow Row { get; }

    public string Name { get; }

    public string CodeText { get; }

    public string CategoryName { get; }

    public string UnitSymbol { get; }

    public string PriceText { get; }

    public string StockText { get; }

    /// <summary>«۲۹ عدد» — the stock column carries its unit, so the list needs no column just for it.</summary>
    public string StockWithUnitText => $"{StockText} {UnitSymbol}";

    /// <summary>Zero or negative stock is drawn in the warning colour, like the sales screen does.</summary>
    public bool IsOutOfStock { get; }

    /// <summary>What a screen reader says for the row (the row is not a control with its own text).</summary>
    public string AccessibleName =>
        $"{Name}، دسته {CategoryName}، قیمت {PriceText} تومان، موجودی {StockText} {UnitSymbol}";
}

/// <summary>One entry of the group panel beside the list. <see cref="Id"/> null means «همه گروه‌ها».</summary>
public sealed record ProductGroupItem(CategoryId? Id, string Name, int Depth)
{
    public string DisplayName => new string(' ', Depth * 3) + Name;
}

/// <summary>
/// «لیست کالاها» (checklist «م», step 2): every active product with search, a group panel
/// and totals. New and edit open the product form (the page does the navigation, this
/// only says which row is selected); «حذف» asks first and then archives — the product
/// leaves the lists and the till but its sales history stays (rule 15 / §3.10).
/// </summary>
public sealed partial class ProductListViewModel : ObservableObject
{
    private static readonly ProductGroupItem AllGroups = new(null, "همه گروه‌ها", 0);

    private readonly IListProductsHandler _list;
    private readonly IArchiveProductHandler _archive;
    private readonly ICatalogLookupReader _lookup;
    private CancellationTokenSource? _searchCancellation;

    public ProductListViewModel(
        IListProductsHandler list,
        IArchiveProductHandler archive,
        ICatalogLookupReader lookup)
    {
        _list = list;
        _archive = archive;
        _lookup = lookup;
    }

    /// <summary>How long typing must pause before the list is re-read. Tests set it to zero.</summary>
    public TimeSpan SearchDelay { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Where an unexpected failure is written (the app's log). Null only in tests, where it should surface.</summary>
    public Action<Exception>? ReportUnexpectedError { get; set; }

    public ObservableCollection<ProductListItem> Items { get; } = [];

    public ObservableCollection<ProductGroupItem> Groups { get; } = [];

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial ProductGroupItem SelectedGroup { get; set; } = AllGroups;

    [ObservableProperty]
    public partial ProductListItem? SelectedItem { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    public partial string CountText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StockTotalText { get; set; } = string.Empty;

    /// <summary>Filled only when more products matched than fit on screen — an honest hint, not a silent cut.</summary>
    [ObservableProperty]
    public partial string TruncatedText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial string? Notice { get; set; }

    [ObservableProperty]
    public partial bool IsArchiveConfirmOpen { get; set; }

    [ObservableProperty]
    public partial string ArchiveConfirmText { get; set; } = string.Empty;

    public bool HasSelection => SelectedItem is not null;

    partial void OnSelectedItemChanged(ProductListItem? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        ArchiveCommand.NotifyCanExecuteChanged();
    }

    partial void OnSearchTextChanged(string value) => _ = SearchAfterDelayAsync();

    partial void OnSelectedGroupChanged(ProductGroupItem value) => _ = SearchAfterDelayAsync();

    /// <summary>First open: the group panel and the list.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await _lookup.LoadAsync(cancellationToken);
            Groups.Clear();
            Groups.Add(AllGroups);
            foreach (var group in OrderAsTree(snapshot.Categories))
            {
                Groups.Add(group);
            }

            await RefreshAsync(cancellationToken);
        }
        catch (Exception exception) when (ReportUnexpectedError is not null)
        {
            ReportUnexpectedError(exception);
            ErrorMessage = "لیست کالاها خوانده نشد و خطا برای پشتیبانی ثبت شد. دوباره امتحان کنید.";
        }
    }

    /// <summary>Re-reads the list for the current search text and group. Keeps the selection when that product is still listed.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var keep = SelectedItem?.Row.Id;
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var page = await _list.ExecuteAsync(
                new ProductListQuery(SearchText, SelectedGroup.Id),
                cancellationToken);

            Items.Clear();
            foreach (var row in page.Rows)
            {
                Items.Add(new ProductListItem(row));
            }

            SelectedItem = Items.FirstOrDefault(item => item.Row.Id == keep);
            IsEmpty = Items.Count == 0;
            CountText = PersianNumber.FormatGrouped(page.TotalCount);
            StockTotalText = SalesText.Quantity(page.TotalStock);
            TruncatedText = page.TotalCount > page.Rows.Count
                ? $"فقط {PersianNumber.FormatGrouped(page.Rows.Count)} کالای اول از {PersianNumber.FormatGrouped(page.TotalCount)} نشان داده شد؛ جست‌وجو را دقیق‌تر کنید."
                : string.Empty;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task SearchAfterDelayAsync()
    {
        _searchCancellation?.Cancel();
        var source = new CancellationTokenSource();
        _searchCancellation = source;

        try
        {
            if (SearchDelay > TimeSpan.Zero)
            {
                await Task.Delay(SearchDelay, source.Token);
            }

            await RefreshAsync(source.Token);
        }
        catch (OperationCanceledException)
        {
            // a newer keystroke replaced this search — nothing to do
        }
        catch (Exception exception) when (ReportUnexpectedError is not null)
        {
            ReportUnexpectedError(exception);
            ErrorMessage = "لیست خوانده نشد و خطا برای پشتیبانی ثبت شد.";
        }
    }

    /// <summary>«حذف (F4)»: asks first. Nothing is archived until <see cref="ConfirmArchiveCommand"/>.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Archive()
    {
        if (SelectedItem is not { } item)
        {
            return;
        }

        ArchiveConfirmText =
            $"«{item.Name}» از لیست کالاها و صندوق برداشته شود؟ سابقه‌ی فروش و موجودی این کالا در گزارش‌ها می‌ماند.";
        IsArchiveConfirmOpen = true;
    }

    [RelayCommand]
    private void CancelArchive() => IsArchiveConfirmOpen = false;

    [RelayCommand]
    private async Task ConfirmArchiveAsync()
    {
        IsArchiveConfirmOpen = false;
        if (SelectedItem is not { } item)
        {
            return;
        }

        try
        {
            var result = await _archive.ExecuteAsync(new ArchiveProductCommand(item.Row.Id), CancellationToken.None);
            if (result.IsSuccess)
            {
                Notice = $"«{item.Name}» حذف شد.";
                SelectedItem = null;
                await RefreshAsync(CancellationToken.None);
            }
            else
            {
                ErrorMessage = result.Error?.Message;
            }
        }
        catch (Exception exception) when (ReportUnexpectedError is not null)
        {
            ReportUnexpectedError(exception);
            ErrorMessage = "حذف انجام نشد و خطا برای پشتیبانی ثبت شد. دوباره امتحان کنید.";
        }
    }

    /// <summary>Puts the categories in tree order (parent, then its children under it), each with its depth for indentation.</summary>
    private static List<ProductGroupItem> OrderAsTree(IReadOnlyList<CategoryLookupItem> categories)
    {
        var byParent = categories
            .Where(category => category.ParentId is not null)
            .GroupBy(category => category.ParentId!.Value)
            .ToDictionary(group => group.Key, group => group.OrderBy(category => category.SortOrder).ThenBy(category => category.Name).ToList());
        var ids = categories.Select(category => category.Id).ToHashSet();

        // A category whose parent is missing is shown at the top level rather than lost.
        var roots = categories
            .Where(category => category.ParentId is null || !ids.Contains(category.ParentId.Value))
            .OrderBy(category => category.SortOrder)
            .ThenBy(category => category.Name);

        var result = new List<ProductGroupItem>();
        foreach (var root in roots)
        {
            Walk(root, 0);
        }

        return result;

        void Walk(CategoryLookupItem category, int depth)
        {
            result.Add(new ProductGroupItem(category.Id, category.Name, depth));
            if (byParent.TryGetValue(category.Id, out var children))
            {
                foreach (var child in children)
                {
                    Walk(child, depth + 1);
                }
            }
        }
    }
}
