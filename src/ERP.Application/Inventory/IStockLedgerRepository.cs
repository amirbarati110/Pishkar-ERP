using ERP.Domain.Catalog;
using ERP.Domain.Inventory;

namespace ERP.Application.Inventory;

public interface IStockLedgerRepository
{
    Task<StockLedger?> GetAsync(
        ProductId productId,
        WarehouseId warehouseId,
        CancellationToken cancellationToken);

    Task AddAsync(StockLedger ledger, CancellationToken cancellationToken);
}

