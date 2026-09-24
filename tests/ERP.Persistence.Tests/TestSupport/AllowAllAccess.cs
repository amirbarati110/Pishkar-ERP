using ERP.Application.Common;
using ERP.Application.Identity;
using ERP.Domain.Identity;

namespace ERP.Persistence.Tests.TestSupport;

/// <summary>
/// For tests about what an operation does, not who may do it — those use the real
/// <c>FirebirdAccessChecker</c> (see <c>AccessEndToEndTests</c>).
/// </summary>
internal sealed class AllowAllAccess : IAccessChecker
{
    public static AllowAllAccess Instance { get; } = new();

    public Task<ApplicationError?> CheckAsync(AccessRight right, CancellationToken cancellationToken) =>
        Task.FromResult<ApplicationError?>(null);
}
