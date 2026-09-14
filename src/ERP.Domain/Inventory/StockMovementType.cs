namespace ERP.Domain.Inventory;

public enum StockMovementType
{
    OpeningBalance = 1,
    Receipt = 2,
    Sale = 3,
    Return = 4,
    AdjustmentIncrease = 5,
    AdjustmentDecrease = 6,
}

