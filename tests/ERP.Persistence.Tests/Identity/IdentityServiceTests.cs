using ERP.Application.Identity;
using ERP.Domain.Identity;
using ERP.Persistence.Database;
using ERP.Persistence.Identity;
using ERP.Persistence.Services;
using ERP.Persistence.Tests.TestSupport;

namespace ERP.Persistence.Tests.Identity;

public sealed class IdentityServiceTests
{
    [Fact]
    public async Task TheFirstAdminSurvivesARoundTripAndCanSignIn()
    {
        await using var database = FirebirdTestDatabase.Create();
        var (factory, service) = await SetupAsync(database);

        var registered = await service.ExecuteAsync(
            new RegisterFirstAdminCommand("owner", "صاحب فروشگاه", "PassWord123"), CancellationToken.None);
        Assert.True(registered.IsSuccess);

        await using var unitOfWork = await FirebirdUnitOfWork.CreateAsync(factory, CancellationToken.None);
        var reloaded = await new FirebirdUserRepository(unitOfWork).GetByIdAsync(registered.Value, CancellationToken.None);
        Assert.NotNull(reloaded);
        Assert.Equal("owner", reloaded!.Username);
        Assert.Equal(UserRole.Admin, reloaded.Role);

        var signedIn = await service.ExecuteAsync(new SignInCommand("owner", "PassWord123"), CancellationToken.None);
        Assert.True(signedIn.IsSuccess);
        Assert.Equal(registered.Value, signedIn.Value?.UserId);
    }

    [Fact]
    public async Task ASecondFirstAdminIsRefusedOnceOneExists()
    {
        await using var database = FirebirdTestDatabase.Create();
        var (_, service) = await SetupAsync(database);
        await service.ExecuteAsync(
            new RegisterFirstAdminCommand("owner", "صاحب فروشگاه", "PassWord123"), CancellationToken.None);

        var second = await service.ExecuteAsync(
            new RegisterFirstAdminCommand("intruder", "کاربر دیگر", "PassWord123"), CancellationToken.None);

        Assert.False(second.IsSuccess);
        Assert.Equal("identity.setup.already-done", second.Error?.Code);
    }

    [Fact]
    public async Task TheDatabaseConstraintCatchesADuplicateUsernameThatSlipsPastTheApplicationCheck()
    {
        // Simulates two setup screens racing: both pass the "no admin yet"
        // check, then both insert. The database is the real referee.
        await using var database = FirebirdTestDatabase.Create();
        var factory = new FirebirdConnectionFactory(database.Options);
        await new FirebirdDatabaseBootstrapper(factory).InitializeAsync(CancellationToken.None);

        await using var first = await FirebirdUnitOfWork.CreateAsync(factory, CancellationToken.None);
        await new FirebirdUserRepository(first).SaveAsync(
            User.Create("owner", "اولی", "PassWord123", UserRole.Admin), CancellationToken.None);
        await first.CommitAsync(CancellationToken.None);

        await using var second = await FirebirdUnitOfWork.CreateAsync(factory, CancellationToken.None);
        await Assert.ThrowsAnyAsync<Exception>(() =>
            new FirebirdUserRepository(second).SaveAsync(
                User.Create("owner", "دومی", "PassWord123", UserRole.Admin), CancellationToken.None));
    }

    [Fact]
    public async Task AnAdminCanCreateACashierWhoCanThenSignIn()
    {
        await using var database = FirebirdTestDatabase.Create();
        var (_, service) = await SetupAsync(database);
        var admin = await service.ExecuteAsync(
            new RegisterFirstAdminCommand("owner", "صاحب فروشگاه", "PassWord123"), CancellationToken.None);

        var created = await service.ExecuteAsync(
            new CreateUserCommand(admin.Value, "cashier1", "صندوق‌دار اول", "PassWord123", UserRole.Cashier),
            CancellationToken.None);
        Assert.True(created.IsSuccess);

        var signedIn = await service.ExecuteAsync(new SignInCommand("cashier1", "PassWord123"), CancellationToken.None);
        Assert.True(signedIn.IsSuccess);
        Assert.Equal(UserRole.Cashier, signedIn.Value?.Role);
    }

    [Fact]
    public async Task VerifyAdminCredentialRejectsACashierAndAcceptsTheRealAdmin()
    {
        await using var database = FirebirdTestDatabase.Create();
        var (_, service) = await SetupAsync(database);
        var admin = await service.ExecuteAsync(
            new RegisterFirstAdminCommand("owner", "صاحب فروشگاه", "PassWord123"), CancellationToken.None);
        await service.ExecuteAsync(
            new CreateUserCommand(admin.Value, "cashier1", "صندوق‌دار اول", "PassWord123", UserRole.Cashier),
            CancellationToken.None);

        var cashierAttempt = await service.ExecuteAsync(
            new VerifyAdminCredentialCommand("cashier1", "PassWord123"), CancellationToken.None);
        var adminAttempt = await service.ExecuteAsync(
            new VerifyAdminCredentialCommand("owner", "PassWord123"), CancellationToken.None);

        Assert.False(cashierAttempt.IsSuccess);
        Assert.True(adminAttempt.IsSuccess);
        Assert.Equal(admin.Value, adminAttempt.Value);
    }

    private static async Task<(FirebirdConnectionFactory Factory, FirebirdIdentityService Service)> SetupAsync(
        FirebirdTestDatabase database)
    {
        var factory = new FirebirdConnectionFactory(database.Options);
        await new FirebirdDatabaseBootstrapper(factory).InitializeAsync(CancellationToken.None);
        var service = new FirebirdIdentityService(factory);
        return (factory, service);
    }
}
