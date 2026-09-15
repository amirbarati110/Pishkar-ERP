using ERP.Domain.Catalog;
using ERP.Domain.Inventory;

namespace ERP.Persistence.Services;

public sealed record RetailSetupDefaults(
    CategoryId GeneralCategoryId,
    UnitId EachUnitId,
    WarehouseId MainWarehouseId);
