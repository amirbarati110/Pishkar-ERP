using ERP.Domain.Catalog;
using ERP.Domain.Common;

namespace ERP.Domain.Inventory;

public sealed class StockLedger
{
    private readonly List<InventoryLayer> _layers = [];
    private readonly List<StockMovement> _movements = [];

    private StockLedger(ProductId productId, WarehouseId warehouseId)
    {
        ProductId = productId;
        WarehouseId = warehouseId;
    }

    public ProductId ProductId { get; }

    public WarehouseId WarehouseId { get; }

    public IReadOnlyList<InventoryLayer> Layers => _layers.AsReadOnly();

    public IReadOnlyList<StockMovement> Movements => _movements.AsReadOnly();

    public StockQuantity AvailableQuantity => StockQuantity.From(_layers.Sum(layer => layer.RemainingQuantity.Value));

    public static StockLedger Empty(ProductId productId, WarehouseId warehouseId)
    {
        return new StockLedger(productId, warehouseId);
    }

    public void ReceiveOpeningStock(Quantity quantity, Money unitCost, DateOnly receivedOn)
    {
        if (receivedOn > DateOnly.FromDateTime(DateTime.UtcNow))
        {
            throw new DomainException("تاریخ موجودی اولیه نمی‌تواند در آینده باشد.");
        }

        var layer = new InventoryLayer(InventoryLayerId.New(), quantity, unitCost, receivedOn);
        _layers.Add(layer);
        _movements.Add(new StockMovement(
            StockMovementId.New(),
            ProductId,
            WarehouseId,
            StockMovementType.OpeningBalance,
            quantity,
            $"opening:{layer.Id}",
            DateTimeOffset.UtcNow));
    }

    public IReadOnlyList<InventoryAllocation> ConsumeFifo(Quantity quantity, string reference)
    {
        var normalizedReference = NormalizeReference(reference);

        if (quantity.Value > AvailableQuantity.Value)
        {
            throw new InsufficientStockException(AvailableQuantity.Value);
        }

        var remaining = quantity.Value;
        var allocations = new List<InventoryAllocation>();

        foreach (var layer in _layers.OrderBy(layer => layer.ReceivedOn))
        {
            if (remaining == 0)
            {
                break;
            }

            if (layer.RemainingQuantity.Value == 0)
            {
                continue;
            }

            var amount = Math.Min(remaining, layer.RemainingQuantity.Value);
            layer.Consume(amount);
            allocations.Add(new InventoryAllocation(layer.Id, Quantity.Create(amount), layer.UnitCost));
            remaining -= amount;
        }

        _movements.Add(new StockMovement(
            StockMovementId.New(),
            ProductId,
            WarehouseId,
            StockMovementType.Sale,
            quantity,
            normalizedReference,
            DateTimeOffset.UtcNow));

        return allocations.AsReadOnly();
    }

    private static string NormalizeReference(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            throw new DomainException("مرجع گردش موجودی را وارد کنید.");
        }

        return reference.Trim();
    }
}
