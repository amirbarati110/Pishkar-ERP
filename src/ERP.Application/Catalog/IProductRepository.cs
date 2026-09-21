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
    /// Persists changes to an existing product: name, code, category, price and
    /// status (the price from the invoice row editor when the cashier explicitly
    /// asks for it, §6.11; the rest from «لیست کالاها»). Barcodes are not re-written
    /// here: they are append-only and have their own uniqueness handling in
    /// <see cref="AddAsync"/>. A duplicate code throws <c>DataConflictException</c>.
    /// </summary>
    Task UpdateAsync(Product product, CancellationToken cancellationToken);
}
