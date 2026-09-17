using ERP.Application.Common;
using ERP.Domain.Identity;

namespace ERP.Application.Identity;

public sealed record VerifyAdminCredentialCommand(string Username, string Password);

public interface IVerifyAdminCredentialHandler
{
    Task<Result<UserId>> ExecuteAsync(VerifyAdminCredentialCommand command, CancellationToken cancellationToken);
}

/// <summary>
/// «تأیید مدیر» (§6.7 credit-over-limit, checklist appendix د.۲: «هنوز نقش را
/// چک نمی‌کند»). Deliberately not a session switch — the cashier stays signed
/// in and keeps working; a manager types <i>their own</i> username/password
/// once, right there, to approve one specific action (§15.3 «PIN شخصی، رمز
/// مشترک ممنوع» — each manager's own credential, never one shared code typed
/// by whoever is at the till).
/// </summary>
public sealed class VerifyAdminCredentialHandler : IVerifyAdminCredentialHandler
{
    private const string RejectionMessage = "نام کاربری یا رمز عبور مدیر درست نیست.";

    private readonly IUserRepository _users;

    public VerifyAdminCredentialHandler(IUserRepository users)
    {
        _users = users;
    }

    public async Task<Result<UserId>> ExecuteAsync(VerifyAdminCredentialCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var user = await _users.GetByUsernameAsync(command.Username?.Trim() ?? string.Empty, cancellationToken).ConfigureAwait(false);
        if (user is null || user.Role != UserRole.Admin || !user.VerifyPassword(command.Password))
        {
            return Result.Failure<UserId>("identity.verify-admin.rejected", RejectionMessage);
        }

        return Result.Success(user.Id);
    }
}
