using ERP.Application.Common;
using ERP.Domain.Identity;

namespace ERP.Application.Identity;

public sealed record SignInCommand(string Username, string Password);

/// <summary>What the app needs after a successful sign-in — enough to fill <c>IUserContext</c> and show the person's own name (§3 — no shared logins, §15.3).</summary>
public sealed record SignedInUser(
    UserId UserId,
    string DisplayName,
    UserRole Role,
    CashierPermissions Permissions = CashierPermissions.All)
{
    /// <summary>For hiding what this person may not use; the server checks again on every call (§10).</summary>
    public bool Can(AccessRight right) => AccessRules.Allows(Role, Permissions, right);
}

public interface ISignInHandler
{
    Task<Result<SignedInUser>> ExecuteAsync(SignInCommand command, CancellationToken cancellationToken);
}

/// <summary>
/// The login screen's only use case. Deliberately gives the same generic
/// "نام کاربری یا رمز عبور اشتباه است" whether the username does not exist,
/// the password is wrong, or the account is archived — telling those apart
/// would let someone probe which usernames are real.
/// </summary>
public sealed class SignInHandler : ISignInHandler
{
    private const string InvalidCredentialsMessage = "نام کاربری یا رمز عبور اشتباه است.";

    private readonly IUserRepository _users;

    public SignInHandler(IUserRepository users)
    {
        _users = users;
    }

    public async Task<Result<SignedInUser>> ExecuteAsync(SignInCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var user = await _users.GetByUsernameAsync(command.Username?.Trim() ?? string.Empty, cancellationToken).ConfigureAwait(false);
        if (user is null || !user.VerifyPassword(command.Password))
        {
            return Result.Failure<SignedInUser>("identity.sign-in.invalid-credentials", InvalidCredentialsMessage);
        }

        return Result.Success(new SignedInUser(user.Id, user.DisplayName, user.Role, user.Permissions));
    }
}
