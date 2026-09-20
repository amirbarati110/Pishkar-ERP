using CommunityToolkit.Mvvm.ComponentModel;
using ERP.Application.Sales;
using ERP.Domain.Common;
using ERP.Domain.Sales;

namespace ERP.Presentation.Features.Sales;

/// <summary>
/// One invoice row in the return window: how much was sold, how much is
/// already back, and how much the cashier is taking back now. It holds what
/// the cashier picked; the refund amount for it is computed by the server.
/// </summary>
public sealed partial class ReturnLineRow : ObservableObject
{
    public ReturnLineRow(ReturnableLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        Line = line;
        ProductName = line.ProductName;
        SoldText = $"فروخته‌شده {PersianNumber.Format(line.SoldQuantity)} {line.UnitSymbol}"
            + (line.ReturnedQuantity > 0 ? $" · قبلاً مرجوع {PersianNumber.Format(line.ReturnedQuantity)}" : string.Empty);
        RemainingText = line.RemainingQuantity > 0
            ? $"قابل مرجوعی {PersianNumber.Format(line.RemainingQuantity)}"
            : "همه‌اش مرجوع شده";
    }

    public ReturnableLine Line { get; }

    public string ProductName { get; }

    public string SoldText { get; }

    public string RemainingText { get; }

    public bool CanReturn => Line.RemainingQuantity > 0;

    [ObservableProperty]
    public partial decimal Quantity { get; set; }

    /// <summary>0 = برگشت به انبار، 1 = خراب، 2 = ضایعات (the order of the three choices on screen).</summary>
    [ObservableProperty]
    public partial int DispositionIndex { get; set; }

    public string QuantityText => PersianNumber.Format(Quantity);

    public bool IsSelected => Quantity > 0;

    public ReturnDisposition Disposition => DispositionIndex switch
    {
        1 => ReturnDisposition.Damaged,
        2 => ReturnDisposition.Waste,
        _ => ReturnDisposition.ToStock,
    };

    partial void OnQuantityChanged(decimal value)
    {
        OnPropertyChanged(nameof(QuantityText));
        OnPropertyChanged(nameof(IsSelected));
    }
}
