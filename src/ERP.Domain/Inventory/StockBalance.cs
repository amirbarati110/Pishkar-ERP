namespace ERP.Domain.Inventory;

/// <summary>
/// The ledger-level available balance. Unlike a single <see cref="InventoryLayer"/>'s
/// RemainingQuantity (which can never go below zero — a physical batch of goods
/// received cannot be negative), this balance is allowed to go negative: it is
/// what StockLedger reports after a sale is knowingly completed against stock
/// that has not been received/recorded yet ("فروش منفی" — goods left the store
/// before the receiving paperwork caught up). A negative balance is meaningful
/// information for staff, not an error state.
/// </summary>
public readonly record struct StockBalance
{
    private StockBalance(decimal value)
    {
        Value = value;
    }

    public decimal Value { get; }

    public bool IsNegative => Value < 0;

    public static StockBalance From(decimal value) => new(value);
}
