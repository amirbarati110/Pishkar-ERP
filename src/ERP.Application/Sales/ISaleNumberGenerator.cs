using ERP.Domain.Sales;

namespace ERP.Application.Sales;

/// <summary>
/// Hands out the next invoice number. Implementations must guarantee that a
/// number is never issued twice and that later calls always return larger
/// numbers — across cashiers, restarts and failed transactions — because that
/// is what سامانه مودیان requires of the internal invoice serial (see
/// <see cref="SaleNumber"/>). Gaps are acceptable; repeats and going backwards
/// are not.
/// </summary>
public interface ISaleNumberGenerator
{
    Task<SaleNumber> NextAsync(CancellationToken cancellationToken);
}
