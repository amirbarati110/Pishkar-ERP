using ERP.Application.Common;
using ERP.Domain.Identity;

namespace ERP.Application.Identity;

/// <summary>
/// The server-side half of every permission (§10: «هر Use Case حساس مجوز را در Application Layer
/// بررسی می‌کند؛ مخفی‌کردن دکمه به‌تنهایی امنیت محسوب نمی‌شود»). The acting user is read fresh from
/// the database on every check, so a right a manager just took away is gone on the next click,
/// not at the next sign-in.
/// </summary>
public sealed class AccessPolicy
{
    private readonly IUserRepository _users;

    public AccessPolicy(IUserRepository users)
    {
        _users = users;
    }

    /// <returns>Null when allowed; otherwise the refusal to hand back unchanged.</returns>
    public async Task<ApplicationError?> CheckAsync(Guid actingUserId, AccessRight right, CancellationToken cancellationToken)
    {
        var user = await _users.GetByIdAsync(UserId.From(actingUserId), cancellationToken).ConfigureAwait(false);
        return user is not null && user.Can(right)
            ? null
            : new ApplicationError("identity.access.denied", AccessRules.DeniedMessage(right));
    }
}
