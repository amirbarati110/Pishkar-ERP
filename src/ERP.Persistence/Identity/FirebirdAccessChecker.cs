using ERP.Application.Common;
using ERP.Application.Identity;
using ERP.Domain.Identity;
using ERP.Persistence.Database;

namespace ERP.Persistence.Identity;

/// <summary>The signed-in user's rights, read from APP_USER on every check.</summary>
public sealed class FirebirdAccessChecker : IAccessChecker
{
    private readonly FirebirdConnectionFactory _connectionFactory;
    private readonly IUserContext _userContext;

    public FirebirdAccessChecker(FirebirdConnectionFactory connectionFactory, IUserContext userContext)
    {
        _connectionFactory = connectionFactory;
        _userContext = userContext;
    }

    public async Task<ApplicationError?> CheckAsync(AccessRight right, CancellationToken cancellationToken)
    {
        await using var unitOfWork = await FirebirdUnitOfWork.CreateAsync(_connectionFactory, cancellationToken).ConfigureAwait(false);
        return await new AccessPolicy(new FirebirdUserRepository(unitOfWork))
            .CheckAsync(_userContext.UserId, right, cancellationToken)
            .ConfigureAwait(false);
    }
}
