using ERP.Domain.Common;

namespace ERP.Domain.Inventory;

public readonly record struct WarehouseId
{
    private WarehouseId(Guid value)
    {
        Value = value;
    }

    public Guid Value { get; }

    public static WarehouseId New() => new(Guid.NewGuid());

    public static WarehouseId From(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new DomainException("شناسه انبار معتبر نیست.");
        }

        return new WarehouseId(value);
    }

    public override string ToString() => Value.ToString("D");
}

