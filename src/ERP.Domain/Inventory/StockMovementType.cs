namespace ERP.Domain.Inventory;

public enum StockMovementType
{
    OpeningBalance = 1,
    Receipt = 2,
    Sale = 3,
    Return = 4,
    AdjustmentIncrease = 5,
    AdjustmentDecrease = 6,

    /// <summary>
    /// A sale knowingly completed beyond recorded stock (goods physically left
    /// before their receiving was entered). Kept distinct from <see cref="Sale"/>
    /// so reports can single out "needs a stock correction" movements.
    /// </summary>
    BackorderSale = 7,
}

