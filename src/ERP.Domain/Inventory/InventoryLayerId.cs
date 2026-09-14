namespace ERP.Domain.Inventory;

public readonly record struct InventoryLayerId(Guid Value)
{
    public static InventoryLayerId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("D");
}

