using ERP.Application.Common;
using ERP.Domain.Identity;

namespace ERP.Application.Identity;

/// <summary>
/// Asks, for whoever is signed in, whether one <see cref="AccessRight"/> is held — read fresh from
/// the database each time (see <see cref="AccessPolicy"/>). Every sensitive service call goes
/// through it before any work starts; hiding a button is only for comfort.
/// </summary>
public interface IAccessChecker
{
    /// <returns>Null when allowed; otherwise the refusal to show.</returns>
    Task<ApplicationError?> CheckAsync(AccessRight right, CancellationToken cancellationToken);
}
