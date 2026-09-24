using System.Collections.ObjectModel;
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

    /// <summary>
    /// Backordered quantity is derived from the movement log (sum of
    /// <see cref="StockMovementType.BackorderSale"/> movements) rather than
    /// stored separately, so there is exactly one source of truth for it.
    /// There is no offsetting movement type yet — nothing currently nets a
    /// backorder back down when new stock is recorded, because there is no
    /// general "receive purchase" flow to hook that into yet (only the one-time
    /// ReceiveOpeningStock). That reconciliation belongs with Phase 2
    /// Purchasing/Receiving.
    /// </summary>
    private decimal BackorderedQuantity => _movements
        .Where(movement => movement.Type == StockMovementType.BackorderSale)
        .Sum(movement => movement.Quantity.Value);

    public StockBalance AvailableQuantity => StockBalance.From(
        _layers.Sum(layer => layer.RemainingQuantity.Value) - BackorderedQuantity);

    public static StockLedger Empty(ProductId productId, WarehouseId warehouseId)
    {
        return new StockLedger(productId, warehouseId);
    }

    internal static StockLedger Rehydrate(
        ProductId productId,
        WarehouseId warehouseId,
        IEnumerable<InventoryLayer> layers,
        IEnumerable<StockMovement> movements)
    {
        var ledger = new StockLedger(productId, warehouseId);
        ledger._layers.AddRange(layers);
        ledger._movements.AddRange(movements);
        return ledger;
    }

    public void ReceiveOpeningStock(Quantity quantity, Money unitCost, DateOnly receivedOn)
    {
        if (receivedOn > PersianDate.GregorianDateInIran(DateTimeOffset.UtcNow))
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

    /// <summary>
    /// Puts returned goods back on the shelf as a layer of their own, at the
    /// cost they originally left at (not today's cost) so a sale followed by
    /// its return leaves the stock's value exactly where it started. Dated
    /// today, so FIFO sells it after older stock.
    /// </summary>
    public void ReceiveReturn(Quantity quantity, Money unitCost, DateOnly receivedOn, string reference)
    {
        var normalizedReference = NormalizeReference(reference);

        var layer = new InventoryLayer(InventoryLayerId.New(), quantity, unitCost, receivedOn);
        _layers.Add(layer);
        _movements.Add(new StockMovement(
            StockMovementId.New(),
            ProductId,
            WarehouseId,
            StockMovementType.Return,
            quantity,
            normalizedReference,
            DateTimeOffset.UtcNow));
    }

    public IReadOnlyList<InventoryAllocation> ConsumeFifo(Quantity quantity, string reference)
    {
        var normalizedReference = NormalizeReference(reference);

        if (quantity.Value > AvailableQuantity.Value)
        {
            throw new InsufficientStockException(AvailableQuantity.Value);
        }

        var allocations = ConsumeAvailableLayers(quantity.Value);

        _movements.Add(new StockMovement(
            StockMovementId.New(),
            ProductId,
            WarehouseId,
            StockMovementType.Sale,
            quantity,
            normalizedReference,
            DateTimeOffset.UtcNow));

        return allocations;
    }

    /// <summary>
    /// Same intent as <see cref="ConsumeFifo"/>, except a shortfall beyond
    /// recorded stock is allowed instead of throwing — for goods that
    /// physically left before their receiving was entered ("فروش منفی"). The
    /// covered portion is still pulled from real layers FIFO as usual; the
    /// uncovered portion is recorded as a <see cref="StockMovementType.BackorderSale"/>
    /// movement, which is what makes <see cref="AvailableQuantity"/> go
    /// negative until someone corrects the stock record. This must be an
    /// explicit, deliberate caller choice, never a silent default — see
    /// <c>CompleteSaleCommand.AllowNegativeStock</c> in the Application layer.
    /// </summary>
    public IReadOnlyList<InventoryAllocation> ConsumeFifoAllowingBackorder(Quantity quantity, string reference)
    {
        var normalizedReference = NormalizeReference(reference);

        var covered = Math.Min(quantity.Value, Math.Max(0, AvailableQuantity.Value));
        var shortfall = quantity.Value - covered;
        var allocations = covered > 0
            ? ConsumeAvailableLayers(covered)
            : new List<InventoryAllocation>().AsReadOnly();

        if (covered > 0)
        {
            _movements.Add(new StockMovement(
                StockMovementId.New(),
                ProductId,
                WarehouseId,
                StockMovementType.Sale,
                Quantity.Create(covered),
                normalizedReference,
                DateTimeOffset.UtcNow));
        }

        if (shortfall > 0)
        {
            _movements.Add(new StockMovement(
                StockMovementId.New(),
                ProductId,
                WarehouseId,
                StockMovementType.BackorderSale,
                Quantity.Create(shortfall),
                normalizedReference,
                DateTimeOffset.UtcNow));
        }

        return allocations;
    }

    private ReadOnlyCollection<InventoryAllocation> ConsumeAvailableLayers(decimal amount)
    {
        var remaining = amount;
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

            var take = Math.Min(remaining, layer.RemainingQuantity.Value);
            layer.Consume(take);
            allocations.Add(new InventoryAllocation(layer.Id, Quantity.Create(take), layer.UnitCost));
            remaining -= take;
        }

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
