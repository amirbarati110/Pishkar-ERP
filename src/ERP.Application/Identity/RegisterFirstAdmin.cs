using ERP.Application.Common;
using ERP.Domain.Common;
using ERP.Domain.Identity;

namespace ERP.Application.Identity;

public sealed record RegisterFirstAdminCommand(string Username, string DisplayName, string Password);

public interface IRegisterFirstAdminHandler
{
    Task<Result<UserId>> ExecuteAsync(RegisterFirstAdminCommand command, CancellationToken cancellationToken);
}

/// <summary>
/// «مدیر اولیه در راه‌اندازی ساخته می‌شود» (milestone-1 §10). Only reachable
/// while no user exists at all — enforced here too, not just by the setup
/// screen only showing once, so a stray second call (or a future API) can
/// never quietly add a second "first" admin or overwrite the real one.
/// </summary>
public sealed class RegisterFirstAdminHandler : IRegisterFirstAdminHandler
{
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _unitOfWork;

    public RegisterFirstAdminHandler(IUserRepository users, IUnitOfWork unitOfWork)
    {
        _users = users;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<UserId>> ExecuteAsync(RegisterFirstAdminCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await _users.AnyExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<UserId>("identity.setup.already-done", "راه‌اندازی قبلاً انجام شده است.");
        }

        try
        {
            var admin = User.Create(command.Username, command.DisplayName, command.Password, UserRole.Admin);
            await _users.SaveAsync(admin, cancellationToken).ConfigureAwait(false);
            await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);
            return Result.Success(admin.Id);
        }
        catch (DomainException exception)
        {
            return Result.Failure<UserId>("identity.setup.invalid", exception.Message);
        }
    }
}
