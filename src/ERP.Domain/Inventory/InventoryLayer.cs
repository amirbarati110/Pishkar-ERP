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

    internal static InventoryLayer Rehydrate(
        InventoryLayerId id,
        Quantity originalQuantity,
        StockQuantity remainingQuantity,
        Money unitCost,
        DateOnly receivedOn)
    {
        if (remainingQuantity.Value > originalQuantity.Value)
        {
            throw new DomainException("موجودی باقی‌مانده لایه نمی‌تواند از مقدار اولیه بیشتر باشد.");
        }

        var layer = new InventoryLayer(id, originalQuantity, unitCost, receivedOn);
        var consumed = originalQuantity.Value - remainingQuantity.Value;

        if (consumed > 0)
        {
            layer.Consume(consumed);
        }

        return layer;
    }

    internal void Consume(decimal amount)
    {
        if (amount <= 0 || amount > RemainingQuantity.Value)
        {
            throw new DomainException("مقدار برداشت از لایه موجودی معتبر نیست.");
        }

        RemainingQuantity = StockQuantity.From(RemainingQuantity.Value - amount);
    }
}
