using ERP.Application.Audit;
using ERP.Application.Common;
using ERP.Domain.Common;
using ERP.Domain.Identity;

namespace ERP.Application.Identity;

/// <param name="ActingUserId">
/// Who is asking — checked against Admin here, in the Application layer, not
/// only by hiding the "کاربر جدید" button from a cashier's screen (§10: "هر
/// Use Case حساس مجوز را در Application Layer بررسی می‌کند؛ مخفی‌کردن دکمه
/// به‌تنهایی امنیت محسوب نمی‌شود").
/// </param>
public sealed record CreateUserCommand(
    UserId ActingUserId,
    string Username,
    string DisplayName,
    string Password,
    UserRole Role,
    CashierPermissions Permissions = CashierPermissions.All);

public interface ICreateUserHandler
{
    Task<Result<UserId>> ExecuteAsync(CreateUserCommand command, CancellationToken cancellationToken);
}

public sealed class CreateUserHandler : ICreateUserHandler
{
    private readonly IUserRepository _users;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public CreateUserHandler(IUserRepository users, IAuditWriter audit, IUnitOfWork unitOfWork, IClock clock)
    {
        _users = users;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<UserId>> ExecuteAsync(CreateUserCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var actingUser = await _users.GetByIdAsync(command.ActingUserId, cancellationToken).ConfigureAwait(false);
        if (actingUser is null || !actingUser.Can(AccessRight.ManageUsers))
        {
            return Result.Failure<UserId>("identity.create-user.forbidden", "فقط مدیر می‌تواند کاربر تازه بسازد.");
        }

        try
        {
            var candidate = User.Create(command.Username, command.DisplayName, command.Password, command.Role);
            candidate.SetPermissions(command.Permissions);
            var existing = await _users.GetByUsernameAsync(candidate.Username, cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                return Result.Failure<UserId>("identity.create-user.duplicate-username", "این نام کاربری قبلاً گرفته شده است.");
            }

            await _users.SaveAsync(candidate, cancellationToken).ConfigureAwait(false);
            await _audit.WriteAsync(
                UserAudit.Entry(command.ActingUserId, "identity.user.created", candidate, null, UserAudit.Describe(candidate), _clock),
                cancellationToken).ConfigureAwait(false);
            await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);
            return Result.Success(candidate.Id);
        }
        catch (DomainException exception)
        {
            return Result.Failure<UserId>("identity.create-user.invalid", exception.Message);
        }
    }
}
