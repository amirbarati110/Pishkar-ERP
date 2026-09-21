using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ERP.Application.Catalog;
using ERP.Domain.Catalog;
using ERP.Domain.Common;
using ERP.Presentation.Common;

namespace ERP.Presentation.Features.Products;

public partial class ProductEditorViewModel : ObservableObject
{
    private readonly ICreateProductHandler _handler;
    private readonly ICatalogLookupReader _catalogLookup;
    private readonly IUpdateProductHandler? _updateHandler;

    /// <param name="updateHandler">Needed only when the form is used to edit a product from «لیست کالاها».</param>
    public ProductEditorViewModel(
        ICreateProductHandler handler,
        ICatalogLookupReader catalogLookup,
        IUpdateProductHandler? updateHandler = null)
    {
        _handler = handler;
        _catalogLookup = catalogLookup;
        _updateHandler = updateHandler;
    }

    /// <summary>Raised after a successful save that should send the user back to the list.</summary>
    public event EventHandler? SavedAndClosed;

    private ProductId? _editingProductId;

    public bool IsEditing => _editingProductId is not null;

    public string PageTitle => IsEditing ? "ویرایش کالا" : "ثبت کالای جدید";

    public string PageSubtitle => IsEditing
        ? "نام، کد، دسته‌بندی و قیمت را عوض کنید. بارکد و واحد اصلی از اینجا عوض نمی‌شوند."
        : "اطلاعات ضروری برای شروع فروش";

    /// <summary>Opens the form on an existing product (F3 in «لیست کالاها»).</summary>
    public void BeginEdit(ProductListRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        _editingProductId = row.Id;
        Name = row.Name;
        Sku = row.Sku;
        Barcode = row.PrimaryBarcode;
        CategoryId = row.CategoryId;
        BaseUnitId = row.BaseUnitId;
        SalePriceTomansText = PersianNumber.FormatGrouped(row.SalePrice.Rials / 10);
        ClearFeedback();
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(PageSubtitle));
    }

    public ObservableCollection<CategoryLookupItem> Categories { get; } = [];

    public ObservableCollection<UnitLookupItem> Units { get; } = [];

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? Sku { get; set; }

    [ObservableProperty]
    public partial string? Barcode { get; set; }

    [ObservableProperty]
    public partial string SalePriceTomansText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial CategoryId? CategoryId { get; set; }

    [ObservableProperty]
    public partial UnitId? BaseUnitId { get; set; }

    [ObservableProperty]
    public partial string? NameError { get; set; }

    [ObservableProperty]
    public partial string? CategoryError { get; set; }

    [ObservableProperty]
    public partial string? BaseUnitError { get; set; }

    [ObservableProperty]
    public partial string? SalePriceError { get; set; }

    [ObservableProperty]
    public partial string? BarcodeError { get; set; }

    [ObservableProperty]
    public partial string? SkuError { get; set; }

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public async Task LoadAsync(
        CategoryId defaultCategoryId,
        UnitId defaultUnitId,
        CancellationToken cancellationToken)
    {
        var snapshot = await _catalogLookup.LoadAsync(cancellationToken);
        ReplaceWith(Categories, snapshot.Categories);
        ReplaceWith(Units, snapshot.Units);

        CategoryId ??= Categories.Any(item => item.Id == defaultCategoryId)
            ? defaultCategoryId
            : Categories.FirstOrDefault()?.Id;
        BaseUnitId ??= Units.Any(item => item.Id == defaultUnitId)
            ? defaultUnitId
            : Units.FirstOrDefault()?.Id;
    }

    [RelayCommand]
    private async Task SaveAsync() => await SaveCoreAsync();

    /// <summary>«ذخیره» in the list flow: save, then go back to the list.</summary>
    [RelayCommand]
    private async Task SaveAndReturnAsync()
    {
        if (await SaveCoreAsync())
        {
            SavedAndClosed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>«ذخیره و کالای بعدی» (§3.14 fast entry): save, then an empty form — the category and unit are kept.</summary>
    [RelayCommand]
    private async Task SaveAndNextAsync()
    {
        if (IsEditing || !await SaveCoreAsync())
        {
            return;
        }

        var saved = StatusMessage;
        Name = string.Empty;
        Sku = null;
        Barcode = null;
        SalePriceTomansText = string.Empty;
        ClearFeedback();
        StatusMessage = saved;
    }

    private async Task<bool> SaveCoreAsync()
    {
        ClearFeedback();

        if (!TryValidate(out var salePriceTomans))
        {
            return false;
        }

        IsBusy = true;

        try
        {
            if (_editingProductId is { } editingId)
            {
                return await UpdateAsync(editingId, salePriceTomans);
            }

            var barcodes = string.IsNullOrWhiteSpace(Barcode)
                ? Array.Empty<string>()
                : [Barcode.Trim()];
            var result = await _handler.ExecuteAsync(
                new CreateProductCommand(
                    Name,
                    Sku,
                    CategoryId!.Value,
                    BaseUnitId!.Value,
                    Money.FromTomans(salePriceTomans),
                    barcodes),
                CancellationToken.None);

            if (result.IsSuccess)
            {
                StatusMessage = "کالا با موفقیت ساخته شد.";
                return true;
            }

            if (result.Error?.Code == "catalog.product.duplicate-barcode")
            {
                BarcodeError = result.Error.Message;
            }
            else if (result.Error?.Code == "catalog.product.duplicate-sku")
            {
                SkuError = result.Error.Message;
            }
            else
            {
                NameError = result.Error?.Message;
            }

            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<bool> UpdateAsync(ProductId productId, long salePriceTomans)
    {
        if (_updateHandler is null)
        {
            throw new InvalidOperationException("ویرایش کالا بدون IUpdateProductHandler ممکن نیست.");
        }

        var result = await _updateHandler.ExecuteAsync(
            new UpdateProductCommand(productId, Name, Sku, CategoryId!.Value, Money.FromTomans(salePriceTomans)),
            CancellationToken.None);

        if (result.IsSuccess)
        {
            StatusMessage = "تغییرات ذخیره شد.";
            return true;
        }

        if (result.Error?.Code == "catalog.product.duplicate-sku")
        {
            SkuError = result.Error.Message;
        }
        else
        {
            NameError = result.Error?.Message;
        }

        return false;
    }

    private bool TryValidate(out long salePriceTomans)
    {
        salePriceTomans = 0;

        if (string.IsNullOrWhiteSpace(Name))
        {
            NameError = "نام کالا را وارد کنید.";
            return false;
        }

        if (CategoryId is null)
        {
            CategoryError = "دسته‌بندی کالا را انتخاب کنید.";
            return false;
        }

        if (BaseUnitId is null)
        {
            BaseUnitError = "واحد اصلی کالا را انتخاب کنید.";
            return false;
        }

        if (!PersianInputParser.TryParseInt64(SalePriceTomansText, out salePriceTomans) || salePriceTomans < 0)
        {
            SalePriceError = "قیمت فروش را به تومان و بدون اعشار وارد کنید.";
            return false;
        }

        return true;
    }

    private void ClearFeedback()
    {
        NameError = null;
        CategoryError = null;
        BaseUnitError = null;
        SalePriceError = null;
        BarcodeError = null;
        SkuError = null;
        StatusMessage = null;
    }

    private static void ReplaceWith<T>(
        ObservableCollection<T> target,
        IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }
}
