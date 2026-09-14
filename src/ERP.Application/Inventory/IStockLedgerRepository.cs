using ERP.Domain.Catalog;
using ERP.Domain.Inventory;

namespace ERP.Application.Inventory;

public interface IStockLedgerRepository
{
    Task<StockLedger?> GetAsync(
        ProductId productId,
        WarehouseId warehouseId,
        CancellationToken cancellationToken);

    Task SaveAsync(StockLedger ledger, CancellationToken cancellationToken);
}
