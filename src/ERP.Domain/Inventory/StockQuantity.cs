using ERP.Domain.Common;

namespace ERP.Domain.Inventory;

public readonly record struct StockQuantity
{
    private StockQuantity(decimal value)
    {
        Value = value;
    }

    public decimal Value { get; }

    public static StockQuantity From(decimal value)
    {
        if (value < 0)
        {
            throw new DomainException("موجودی نمی‌تواند منفی باشد.");
        }

        return new StockQuantity(value);
    }
}

