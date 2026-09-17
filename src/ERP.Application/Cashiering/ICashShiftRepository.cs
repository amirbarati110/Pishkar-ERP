using ERP.Domain.Cashiering;
using ERP.Domain.Inventory;

namespace ERP.Application.Cashiering;

public interface ICashShiftRepository
{
    /// <summary>The shift currently open for this warehouse, or null if none — how every handler here tells "no shift running yet".</summary>
    Task<CashShift?> GetOpenAsync(WarehouseId warehouseId, CancellationToken cancellationToken);

    Task<CashShift?> GetByIdAsync(CashShiftId id, CancellationToken cancellationToken);

    Task SaveAsync(CashShift shift, CancellationToken cancellationToken);
}
