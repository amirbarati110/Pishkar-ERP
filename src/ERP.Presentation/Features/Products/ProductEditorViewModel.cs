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

    public ProductEditorViewModel(
        ICreateProductHandler handler,
        ICatalogLookupReader catalogLookup)
    {
        _handler = handler;
        _catalogLookup = catalogLookup;
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
    private async Task SaveAsync()
    {
        ClearFeedback();

        if (!TryValidate(out var salePriceTomans))
        {
            return;
        }

        IsBusy = true;

        try
        {
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
            }
            else if (result.Error?.Code == "catalog.product.duplicate-barcode")
            {
                BarcodeError = result.Error.Message;
            }
            else
            {
                NameError = result.Error?.Message;
            }
        }
        finally
        {
            IsBusy = false;
        }
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
