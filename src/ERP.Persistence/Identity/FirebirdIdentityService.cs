using ERP.Application.Common;
using ERP.Application.Identity;
using ERP.Domain.Identity;
using ERP.Persistence.Database;

namespace ERP.Persistence.Identity;

/// <summary>Composition root for the Identity module, one unit of work per call — the same shape as <see cref="Customers.FirebirdCustomerService"/>.</summary>
public sealed class FirebirdIdentityService :
    IRegisterFirstAdminHandler,
    ISignInHandler,
    IVerifyAdminCredentialHandler,
    ICreateUserHandler
{
    private readonly FirebirdConnectionFactory _connectionFactory;

    public FirebirdIdentityService(FirebirdConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <summary>How the app tells "first run, show setup" from "show login" — before any window is on screen.</summary>
    public async Task<bool> AnyUserExistsAsync(CancellationToken cancellationToken)
    {
        await using var unitOfWork = await FirebirdUnitOfWork
            .CreateAsync(_connectionFactory, cancellationToken)
            .ConfigureAwait(false);
        return await new FirebirdUserRepository(unitOfWork).AnyExistsAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<UserId>> ExecuteAsync(RegisterFirstAdminCommand command, CancellationToken cancellationToken)
    {
        await using var unitOfWork = await FirebirdUnitOfWork
            .CreateAsync(_connectionFactory, cancellationToken)
            .ConfigureAwait(false);
        return await new RegisterFirstAdminHandler(new FirebirdUserRepository(unitOfWork), unitOfWork)
            .ExecuteAsync(command, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Result<SignedInUser>> ExecuteAsync(SignInCommand command, CancellationToken cancellationToken)
    {
        await using var unitOfWork = await FirebirdUnitOfWork
            .CreateAsync(_connectionFactory, cancellationToken)
            .ConfigureAwait(false);
        return await new SignInHandler(new FirebirdUserRepository(unitOfWork))
            .ExecuteAsync(command, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Result<UserId>> ExecuteAsync(VerifyAdminCredentialCommand command, CancellationToken cancellationToken)
    {
        await using var unitOfWork = await FirebirdUnitOfWork
            .CreateAsync(_connectionFactory, cancellationToken)
            .ConfigureAwait(false);
        return await new VerifyAdminCredentialHandler(new FirebirdUserRepository(unitOfWork))
            .ExecuteAsync(command, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Result<UserId>> ExecuteAsync(CreateUserCommand command, CancellationToken cancellationToken)
    {
        await using var unitOfWork = await FirebirdUnitOfWork
            .CreateAsync(_connectionFactory, cancellationToken)
            .ConfigureAwait(false);
        return await new CreateUserHandler(new FirebirdUserRepository(unitOfWork), unitOfWork)
            .ExecuteAsync(command, cancellationToken)
            .ConfigureAwait(false);
    }
}
