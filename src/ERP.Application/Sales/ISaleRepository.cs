using ERP.Domain.Sales;

namespace ERP.Application.Sales;

public interface ISaleRepository
{
    Task<Sale?> GetAsync(SaleId saleId, CancellationToken cancellationToken);

    Task SaveAsync(Sale sale, CancellationToken cancellationToken);

    /// <summary>
    /// Whether a «صورتحساب اصلاحی» already points at <paramref name="saleId"/> —
    /// only one correction per sale in this version (§10.10 keeps the chain
    /// simple to follow rather than allowing a correction of a correction).
    /// </summary>
    Task<bool> HasCorrectionAsync(SaleId saleId, CancellationToken cancellationToken);
}
