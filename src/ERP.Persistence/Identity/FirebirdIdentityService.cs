using ERP.Application.Common;
using ERP.Application.Identity;
using ERP.Domain.Identity;
using ERP.Persistence.Audit;
using ERP.Persistence.Database;

namespace ERP.Persistence.Identity;

/// <summary>Composition root for the Identity module, one unit of work per call — the same shape as <see cref="Customers.FirebirdCustomerService"/>.</summary>
public sealed class FirebirdIdentityService :
    IRegisterFirstAdminHandler,
    ISignInHandler,
    IVerifyAdminCredentialHandler,
    ICreateUserHandler,
    IListUsersHandler,
    IUpdateUserHandler,
    IResetUserPasswordHandler,
    IArchiveUserHandler
{
    private readonly FirebirdConnectionFactory _connectionFactory;
    private readonly IClock _clock;

    public FirebirdIdentityService(FirebirdConnectionFactory connectionFactory, IClock? clock = null)
    {
        _connectionFactory = connectionFactory;
        _clock = clock ?? UtcClock.Instance;
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
        return await new CreateUserHandler(new FirebirdUserRepository(unitOfWork), new FirebirdAuditWriter(unitOfWork), unitOfWork, _clock)
            .ExecuteAsync(command, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Result<IReadOnlyList<UserListRow>>> ExecuteAsync(ListUsersQuery query, CancellationToken cancellationToken)
    {
        await using var unitOfWork = await FirebirdUnitOfWork.CreateAsync(_connectionFactory, cancellationToken).ConfigureAwait(false);
        return await new ListUsersHandler(new FirebirdUserRepository(unitOfWork))
            .ExecuteAsync(query, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Result<bool>> ExecuteAsync(UpdateUserCommand command, CancellationToken cancellationToken)
    {
        await using var unitOfWork = await FirebirdUnitOfWork.CreateAsync(_connectionFactory, cancellationToken).ConfigureAwait(false);
        return await new UpdateUserHandler(new FirebirdUserRepository(unitOfWork), new FirebirdAuditWriter(unitOfWork), unitOfWork, _clock)
            .ExecuteAsync(command, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Result<bool>> ExecuteAsync(ResetUserPasswordCommand command, CancellationToken cancellationToken)
    {
        await using var unitOfWork = await FirebirdUnitOfWork.CreateAsync(_connectionFactory, cancellationToken).ConfigureAwait(false);
        return await new ResetUserPasswordHandler(new FirebirdUserRepository(unitOfWork), new FirebirdAuditWriter(unitOfWork), unitOfWork, _clock)
            .ExecuteAsync(command, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Result<bool>> ExecuteAsync(ArchiveUserCommand command, CancellationToken cancellationToken)
    {
        await using var unitOfWork = await FirebirdUnitOfWork.CreateAsync(_connectionFactory, cancellationToken).ConfigureAwait(false);
        return await new ArchiveUserHandler(new FirebirdUserRepository(unitOfWork), new FirebirdAuditWriter(unitOfWork), unitOfWork, _clock)
            .ExecuteAsync(command, cancellationToken)
            .ConfigureAwait(false);
    }

    private sealed class UtcClock : IClock
    {
        public static readonly UtcClock Instance = new();

        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }
}
