using ERP.Domain.Identity;

namespace ERP.Application.Identity;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(UserId id, CancellationToken cancellationToken);

    /// <param name="username">Matched case-insensitively — <see cref="User"/> already stores it lowercased, so the caller just needs to lowercase the typed value the same way.</param>
    Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken);

    /// <summary>Whether any user exists at all — how the app tells "first run, show setup" from "show login".</summary>
    Task<bool> AnyExistsAsync(CancellationToken cancellationToken);

    Task SaveAsync(User user, CancellationToken cancellationToken);

    /// <summary>Every user, active and archived, by display name — for «کاربران».</summary>
    Task<IReadOnlyList<User>> ListAsync(CancellationToken cancellationToken);
}
