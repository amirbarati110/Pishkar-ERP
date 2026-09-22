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
    private readonly IGenerateProductBarcodeHandler? _barcodeGenerator;
    private string? _originalBarcode;

    /// <param name="updateHandler">Needed only when the form is used to edit a product from «لیست کالاها».</param>
    /// <param name="barcodeGenerator">
    /// «ساخت بارکد خودکار»: without it the form works as before and a product without a barcode stays without one.
    /// </param>
    public ProductEditorViewModel(
        ICreateProductHandler handler,
        ICatalogLookupReader catalogLookup,
        IUpdateProductHandler? updateHandler = null,
        IGenerateProductBarcodeHandler? barcodeGenerator = null)
    {
        _handler = handler;
        _catalogLookup = catalogLookup;
        _updateHandler = updateHandler;
        _barcodeGenerator = barcodeGenerator;
    }

    /// <summary>
    /// A barcode can be typed or made for a new product, and for an old one that has none. A product
    /// that already has a barcode keeps it: labels and scanners depend on it staying the same.
    /// </summary>
    public bool IsBarcodeEditable => !IsEditing || string.IsNullOrEmpty(_originalBarcode);

    public bool CanGenerateBarcode => IsBarcodeEditable && _barcodeGenerator is not null;

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
        _originalBarcode = row.PrimaryBarcode;
        CategoryId = row.CategoryId;
        BaseUnitId = row.BaseUnitId;
        SalePriceTomansText = PersianNumber.FormatGrouped(row.SalePrice.Rials / 10);
        ClearFeedback();
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(IsBarcodeEditable));
        OnPropertyChanged(nameof(CanGenerateBarcode));
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

    /// <summary>
    /// «ساخت بارکد خودکار»: fills the barcode box with a new EAN-13 from the shop's own range.
    /// Asking again while the box already holds a code does nothing, so repeated clicks do not use up numbers.
    /// </summary>
    [RelayCommand]
    private async Task GenerateBarcodeAsync()
    {
        BarcodeError = null;
        if (!CanGenerateBarcode)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(Barcode))
        {
            BarcodeError = "بارکد نوشته شده است؛ اگر بارکد تازه می‌خواهید اول آن را پاک کنید.";
            return;
        }

        var result = await _barcodeGenerator!.ExecuteAsync(CancellationToken.None);
        if (result.IsSuccess)
        {
            Barcode = result.Value;
        }
        else
        {
            BarcodeError = result.Error?.Message;
        }
    }

    /// <summary>
    /// A product saved without a barcode gets one made by the shop — every product can then be
    /// scanned and labelled. Returns the message to add to the success text, or null when nothing was made.
    /// </summary>
    private async Task<string?> EnsureBarcodeAsync()
    {
        if (!CanGenerateBarcode || !string.IsNullOrWhiteSpace(Barcode))
        {
            return null;
        }

        var result = await _barcodeGenerator!.ExecuteAsync(CancellationToken.None);
        if (!result.IsSuccess)
        {
            BarcodeError = result.Error?.Message;
            return null;
        }

        Barcode = result.Value;
        return $"بارکد {Barcode} ساخته و ثبت شد.";
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
            var madeBarcode = await EnsureBarcodeAsync();

            if (_editingProductId is { } editingId)
            {
                return await UpdateAsync(editingId, salePriceTomans, madeBarcode);
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
                StatusMessage = madeBarcode is null ? "کالا با موفقیت ساخته شد." : $"کالا با موفقیت ساخته شد. {madeBarcode}";
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

    private async Task<bool> UpdateAsync(ProductId productId, long salePriceTomans, string? madeBarcode)
    {
        if (_updateHandler is null)
        {
            throw new InvalidOperationException("ویرایش کالا بدون IUpdateProductHandler ممکن نیست.");
        }

        var result = await _updateHandler.ExecuteAsync(
            new UpdateProductCommand(
                productId,
                Name,
                Sku,
                CategoryId!.Value,
                Money.FromTomans(salePriceTomans),
                IsBarcodeEditable && !string.IsNullOrWhiteSpace(Barcode) ? Barcode.Trim() : null),
            CancellationToken.None);

        if (result.IsSuccess)
        {
            StatusMessage = madeBarcode is null ? "تغییرات ذخیره شد." : $"تغییرات ذخیره شد. {madeBarcode}";
            return true;
        }

        if (result.Error?.Code == "catalog.product.duplicate-barcode")
        {
            BarcodeError = result.Error.Message;
            return false;
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
