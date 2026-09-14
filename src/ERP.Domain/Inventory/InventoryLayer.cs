using ERP.Domain.Common;

namespace ERP.Domain.Inventory;

public sealed class InventoryLayer
{
    internal InventoryLayer(
        InventoryLayerId id,
        Quantity originalQuantity,
        Money unitCost,
        DateOnly receivedOn)
    {
        Id = id;
        OriginalQuantity = originalQuantity;
        RemainingQuantity = StockQuantity.From(originalQuantity.Value);
        UnitCost = unitCost;
        ReceivedOn = receivedOn;
    }

    public InventoryLayerId Id { get; }

    public Quantity OriginalQuantity { get; }

    public StockQuantity RemainingQuantity { get; private set; }

    public Money UnitCost { get; }

    public DateOnly ReceivedOn { get; }

    internal void Consume(decimal amount)
    {
        if (amount <= 0 || amount > RemainingQuantity.Value)
        {
            throw new DomainException("مقدار برداشت از لایه موجودی معتبر نیست.");
        }

        RemainingQuantity = StockQuantity.From(RemainingQuantity.Value - amount);
    }
}

