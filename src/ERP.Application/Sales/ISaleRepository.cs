using ERP.Domain.Sales;

namespace ERP.Application.Sales;

public interface ISaleRepository
{
    Task<Sale?> GetAsync(SaleId saleId, CancellationToken cancellationToken);

    Task SaveAsync(Sale sale, CancellationToken cancellationToken);
}
