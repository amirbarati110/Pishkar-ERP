using System.Globalization;
using ERP.Application.Audit;
using ERP.Application.Common;
using ERP.Domain.Common;
using ERP.Domain.Identity;

namespace ERP.Application.Identity;

// «کاربران» (appendix ز.۲ — the missing user management; §15.1–15.3): a manager lists people,
// changes a name, role or a cashier's switches, sets a new password, and takes someone out of
// use. Every step is checked here against the acting user, read fresh — not by hiding buttons.
// The store must always keep one active manager: nothing here can remove or demote the last one.

public sealed record UserListRow(
    UserId Id,
    string Username,
    string DisplayName,
    UserRole Role,
    UserStatus Status,
    CashierPermissions Permissions);

public sealed record ListUsersQuery(UserId ActingUserId);

public interface IListUsersHandler
{
    Task<Result<IReadOnlyList<UserListRow>>> ExecuteAsync(ListUsersQuery query, CancellationToken cancellationToken);
}

public sealed class ListUsersHandler : IListUsersHandler
{
    private readonly IUserRepository _users;

    public ListUsersHandler(IUserRepository users)
    {
        _users = users;
    }

    public async Task<Result<IReadOnlyList<UserListRow>>> ExecuteAsync(ListUsersQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (await new AccessPolicy(_users).CheckAsync(query.ActingUserId.Value, AccessRight.ManageUsers, cancellationToken).ConfigureAwait(false) is { } denied)
        {
            return Result.Failure<IReadOnlyList<UserListRow>>(denied.Code, denied.Message);
        }

        var users = await _users.ListAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success<IReadOnlyList<UserListRow>>(users
            .Select(user => new UserListRow(user.Id, user.Username, user.DisplayName, user.Role, user.Status, user.Permissions))
            .ToList());
    }
}

public sealed record UpdateUserCommand(
    UserId ActingUserId,
    UserId UserId,
    string DisplayName,
    UserRole Role,
    CashierPermissions Permissions);

public interface IUpdateUserHandler
{
    Task<Result<bool>> ExecuteAsync(UpdateUserCommand command, CancellationToken cancellationToken);
}

public sealed class UpdateUserHandler : IUpdateUserHandler
{
    private readonly IUserRepository _users;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public UpdateUserHandler(IUserRepository users, IAuditWriter audit, IUnitOfWork unitOfWork, IClock clock)
    {
        _users = users;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<bool>> ExecuteAsync(UpdateUserCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await new AccessPolicy(_users).CheckAsync(command.ActingUserId.Value, AccessRight.ManageUsers, cancellationToken).ConfigureAwait(false) is { } denied)
        {
            return Result.Failure<bool>(denied.Code, denied.Message);
        }

        var user = await _users.GetByIdAsync(command.UserId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return Result.Failure<bool>("identity.user.not-found", "این کاربر پیدا نشد.");
        }

        if (user.Role == UserRole.Admin && command.Role != UserRole.Admin
            && await LastActiveManager.IsAsync(_users, user, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<bool>("identity.user.last-admin", LastActiveManager.Message);
        }

        var before = UserAudit.Describe(user);
        try
        {
            user.Rename(command.DisplayName);
            user.ChangeRole(command.Role);
            user.SetPermissions(command.Permissions);
        }
        catch (DomainException exception)
        {
            return Result.Failure<bool>("identity.user.invalid", exception.Message);
        }

        await _users.SaveAsync(user, cancellationToken).ConfigureAwait(false);
        await _audit.WriteAsync(
            UserAudit.Entry(command.ActingUserId, "identity.user.updated", user, before, UserAudit.Describe(user), _clock),
            cancellationToken).ConfigureAwait(false);
        await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success(true);
    }
}

public sealed record ResetUserPasswordCommand(UserId ActingUserId, UserId UserId, string NewPassword);

public interface IResetUserPasswordHandler
{
    Task<Result<bool>> ExecuteAsync(ResetUserPasswordCommand command, CancellationToken cancellationToken);
}

/// <summary>A manager sets a new password for someone who forgot theirs — the only way back in; there is no e-mail to send a link to.</summary>
public sealed class ResetUserPasswordHandler : IResetUserPasswordHandler
{
    private readonly IUserRepository _users;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public ResetUserPasswordHandler(IUserRepository users, IAuditWriter audit, IUnitOfWork unitOfWork, IClock clock)
    {
        _users = users;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<bool>> ExecuteAsync(ResetUserPasswordCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await new AccessPolicy(_users).CheckAsync(command.ActingUserId.Value, AccessRight.ManageUsers, cancellationToken).ConfigureAwait(false) is { } denied)
        {
            return Result.Failure<bool>(denied.Code, denied.Message);
        }

        var user = await _users.GetByIdAsync(command.UserId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return Result.Failure<bool>("identity.user.not-found", "این کاربر پیدا نشد.");
        }

        try
        {
            user.ChangePassword(command.NewPassword);
        }
        catch (DomainException exception)
        {
            return Result.Failure<bool>("identity.user.invalid-password", exception.Message);
        }

        await _users.SaveAsync(user, cancellationToken).ConfigureAwait(false);
        await _audit.WriteAsync(
            UserAudit.Entry(command.ActingUserId, "identity.user.password-reset", user, null, null, _clock),
            cancellationToken).ConfigureAwait(false);
        await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success(true);
    }
}

public sealed record ArchiveUserCommand(UserId ActingUserId, UserId UserId);

public interface IArchiveUserHandler
{
    Task<Result<bool>> ExecuteAsync(ArchiveUserCommand command, CancellationToken cancellationToken);
}

/// <summary>
/// Takes someone out of use: they can no longer sign in, and their name stays on every invoice and
/// audit row they made. Not oneself (the screen would lock its own user out mid-session) and never
/// the last active manager.
/// </summary>
public sealed class ArchiveUserHandler : IArchiveUserHandler
{
    private readonly IUserRepository _users;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public ArchiveUserHandler(IUserRepository users, IAuditWriter audit, IUnitOfWork unitOfWork, IClock clock)
    {
        _users = users;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<bool>> ExecuteAsync(ArchiveUserCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await new AccessPolicy(_users).CheckAsync(command.ActingUserId.Value, AccessRight.ManageUsers, cancellationToken).ConfigureAwait(false) is { } denied)
        {
            return Result.Failure<bool>(denied.Code, denied.Message);
        }

        if (command.UserId == command.ActingUserId)
        {
            return Result.Failure<bool>("identity.user.self", "نمی‌توانید حساب خودتان را حذف کنید.");
        }

        var user = await _users.GetByIdAsync(command.UserId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return Result.Failure<bool>("identity.user.not-found", "این کاربر پیدا نشد.");
        }

        if (user.Status == UserStatus.Archived)
        {
            return Result.Success(true);
        }

        if (user.Role == UserRole.Admin && await LastActiveManager.IsAsync(_users, user, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<bool>("identity.user.last-admin", LastActiveManager.Message);
        }

        var before = UserAudit.Describe(user);
        user.Archive();
        await _users.SaveAsync(user, cancellationToken).ConfigureAwait(false);
        await _audit.WriteAsync(
            UserAudit.Entry(command.ActingUserId, "identity.user.archived", user, before, UserAudit.Describe(user), _clock),
            cancellationToken).ConfigureAwait(false);
        await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success(true);
    }
}

internal static class LastActiveManager
{
    public const string Message = "این تنها مدیر فعال است؛ برنامه همیشه به یک مدیر نیاز دارد. اول یک مدیر دیگر بسازید.";

    public static async Task<bool> IsAsync(IUserRepository users, User user, CancellationToken cancellationToken)
    {
        var all = await users.ListAsync(cancellationToken).ConfigureAwait(false);
        return !all.Any(other => other.Id != user.Id && other.Role == UserRole.Admin && other.Status == UserStatus.Active);
    }
}

/// <summary>
/// Every change to a person's access is a «Security Action» (§15.4): who did it, to whom, when, and
/// the role/switches before and after. A password is never written — only that it was reset.
/// </summary>
internal static class UserAudit
{
    public static string Describe(User user) => string.Create(
        CultureInfo.InvariantCulture,
        $"name={user.DisplayName};role={user.Role};permissions={user.Permissions};status={user.Status}");

    public static AuditEntry Entry(UserId actor, string action, User user, string? before, string? after, IClock clock) =>
        new(Guid.NewGuid(), actor.Value, action, nameof(User), user.Id.ToString(), before, after, clock.UtcNow);
}
