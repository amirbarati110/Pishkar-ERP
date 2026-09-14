using ERP.Domain.Common;

namespace ERP.Domain.Inventory;

public sealed record InventoryAllocation(
    InventoryLayerId LayerId,
    Quantity Quantity,
    Money UnitCost);

