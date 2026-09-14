using ERP.Domain.Catalog;
using ERP.Domain.Common;

namespace ERP.Domain.Inventory;

public sealed record StockMovement(
    StockMovementId Id,
    ProductId ProductId,
    WarehouseId WarehouseId,
    StockMovementType Type,
    Quantity Quantity,
    string Reference,
    DateTimeOffset OccurredAtUtc);

