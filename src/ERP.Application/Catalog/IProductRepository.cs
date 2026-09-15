using ERP.Domain.Catalog;

namespace ERP.Application.Catalog;

public interface IProductRepository
{
    Task<Product?> GetByIdAsync(
        ProductId productId,
        CancellationToken cancellationToken);

    Task<bool> BarcodeExistsAsync(string barcode, CancellationToken cancellationToken);

    Task AddAsync(Product product, CancellationToken cancellationToken);

    /// <summary>
    /// Persists changes to an existing product — today only the master price,
    /// changed from the invoice row editor when the cashier explicitly asks for
    /// it (§6.11). Barcodes are not re-written here: they are append-only and
    /// have their own uniqueness handling in <see cref="AddAsync"/>.
    /// </summary>
    Task UpdateAsync(Product product, CancellationToken cancellationToken);
}
