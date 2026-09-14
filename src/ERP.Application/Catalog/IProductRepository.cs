using ERP.Domain.Catalog;

namespace ERP.Application.Catalog;

public interface IProductRepository
{
    Task<bool> BarcodeExistsAsync(string barcode, CancellationToken cancellationToken);

    Task AddAsync(Product product, CancellationToken cancellationToken);
}

