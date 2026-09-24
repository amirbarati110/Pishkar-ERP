using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ERP.Application.Catalog;
using ERP.Application.Inventory;
using ERP.Domain.Catalog;
using ERP.Domain.Common;
using ERP.Domain.Inventory;
using ERP.Presentation.Common;

namespace ERP.Presentation.Features.Inventory;

public partial class OpeningStockViewModel : ObservableObject
{
    private readonly IReceiveOpeningStockHandler _handler;
    private readonly ICatalogLookupReader _catalogLookup;

    public OpeningStockViewModel(
        IReceiveOpeningStockHandler handler,
        ICatalogLookupReader catalogLookup)
    {
        _handler = handler;
        _catalogLookup = catalogLookup;
    }

    public ObservableCollection<ProductLookupItem> Products { get; } = [];

    [ObservableProperty]
    public partial ProductId? ProductId { get; set; }

    [ObservableProperty]
    public partial WarehouseId? WarehouseId { get; set; }

    [ObservableProperty]
    public partial string QuantityText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string UnitCostTomansText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial DateOnly ReceivedOn { get; set; } = PersianDate.GregorianDateInIran(DateTimeOffset.UtcNow);

    [ObservableProperty]
    public partial string? ProductError { get; set; }

    [ObservableProperty]
    public partial string? WarehouseError { get; set; }

    [ObservableProperty]
    public partial string? QuantityError { get; set; }

    [ObservableProperty]
    public partial string? UnitCostError { get; set; }

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        var snapshot = await _catalogLookup.LoadAsync(cancellationToken);
        Products.Clear();
        foreach (var product in snapshot.Products)
        {
            Products.Add(product);
        }

        ProductId ??= Products.FirstOrDefault()?.Id;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ClearFeedback();

        if (!TryValidate(out var quantity, out var unitCostTomans))
        {
            return;
        }

        IsBusy = true;

        try
        {
            var result = await _handler.ExecuteAsync(
                new ReceiveOpeningStockCommand(
                    ProductId!.Value,
                    WarehouseId!.Value,
                    quantity,
                    checked(unitCostTomans * 10),
                    ReceivedOn),
                CancellationToken.None);

            if (result.IsSuccess)
            {
                StatusMessage = "موجودی اولیه با موفقیت ثبت شد.";
            }
            else
            {
                QuantityError = result.Error?.Message;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool TryValidate(out decimal quantity, out long unitCostTomans)
    {
        quantity = 0;
        unitCostTomans = 0;

        if (ProductId is null)
        {
            ProductError = "کالا را انتخاب کنید.";
            return false;
        }

        if (WarehouseId is null)
        {
            WarehouseError = "انبار را انتخاب کنید.";
            return false;
        }

        if (!PersianInputParser.TryParseDecimal(QuantityText, out quantity) || quantity <= 0)
        {
            QuantityError = "تعداد باید بیشتر از صفر باشد.";
            return false;
        }

        if (!PersianInputParser.TryParseInt64(UnitCostTomansText, out unitCostTomans) || unitCostTomans < 0)
        {
            UnitCostError = "بهای واحد را به تومان و بدون اعشار وارد کنید.";
            return false;
        }

        return true;
    }

    private void ClearFeedback()
    {
        ProductError = null;
        WarehouseError = null;
        QuantityError = null;
        UnitCostError = null;
        StatusMessage = null;
    }
}
