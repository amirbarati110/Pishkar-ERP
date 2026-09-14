namespace ERP.Domain.Inventory;

public readonly record struct StockMovementId(Guid Value)
{
    public static StockMovementId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("D");
}

