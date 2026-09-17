using ERP.Application.Common;
using ERP.Application.Identity;
using ERP.Domain.Identity;

namespace ERP.Desktop.Tests.Features.Identity;

/// <summary>The real Application handlers over in-memory storage, same shape as <see cref="Sales.InMemorySalesBackend"/>.</summary>
internal sealed class InMemoryIdentityBackend : IUserRepository, IUnitOfWork
{
    public List<User> Users { get; } = [];

    public int CommitCount { get; private set; }

    public Task<User?> GetByIdAsync(UserId id, CancellationToken cancellationToken) =>
        Task.FromResult(Users.SingleOrDefault(user => user.Id == id));

    public Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken) =>
        Task.FromResult(Users.SingleOrDefault(user =>
            string.Equals(user.Username, username?.Trim(), StringComparison.OrdinalIgnoreCase)));

    public Task<bool> AnyExistsAsync(CancellationToken cancellationToken) => Task.FromResult(Users.Count > 0);

    public Task SaveAsync(User user, CancellationToken cancellationToken)
    {
        if (!Users.Contains(user))
        {
            Users.Add(user);
        }

        return Task.CompletedTask;
    }

    public Task CommitAsync(CancellationToken cancellationToken)
    {
        CommitCount++;
        return Task.CompletedTask;
    }

    public RegisterFirstAdminHandler RegisterFirstAdmin => new(this, this);

    public SignInHandler SignIn => new(this);

    public VerifyAdminCredentialHandler VerifyAdminCredential => new(this);

    public CreateUserHandler CreateUser => new(this, this);
}
