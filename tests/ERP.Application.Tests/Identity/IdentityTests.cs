using ERP.Application.Identity;
using ERP.Application.Tests.TestDoubles;
using ERP.Domain.Identity;

namespace ERP.Application.Tests.Identity;

public sealed class RegisterFirstAdminTests
{
    [Fact]
    public async Task SucceedsWhenNoUserExistsYet()
    {
        var context = new ApplicationTestContext();

        var result = await Handler(context).ExecuteAsync(
            new RegisterFirstAdminCommand("owner", "صاحب فروشگاه", "PassWord123"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var saved = Assert.Single(context.Users.Items);
        Assert.Equal(UserRole.Admin, saved.Role);
        Assert.Equal(1, context.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task FailsWhenAUserAlreadyExists()
    {
        var context = new ApplicationTestContext();
        context.Users.Items.Add(User.Create("existing", "کاربر قبلی", "PassWord123", UserRole.Cashier));

        var result = await Handler(context).ExecuteAsync(
            new RegisterFirstAdminCommand("owner", "صاحب فروشگاه", "PassWord123"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("identity.setup.already-done", result.Error?.Code);
    }

    [Fact]
    public async Task FailsWithAShortPasswordAndSavesNothing()
    {
        var context = new ApplicationTestContext();

        var result = await Handler(context).ExecuteAsync(
            new RegisterFirstAdminCommand("owner", "صاحب فروشگاه", "short"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("identity.setup.invalid", result.Error?.Code);
        Assert.Empty(context.Users.Items);
    }

    private static RegisterFirstAdminHandler Handler(ApplicationTestContext context) =>
        new(context.Users, context.UnitOfWork);
}

public sealed class SignInTests
{
    [Fact]
    public async Task SucceedsWithTheRightPassword()
    {
        var context = new ApplicationTestContext();
        var user = User.Create("cashier1", "صندوق‌دار اول", "PassWord123", UserRole.Cashier);
        context.Users.Items.Add(user);

        var result = await Handler(context).ExecuteAsync(
            new SignInCommand("cashier1", "PassWord123"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(user.Id, result.Value?.UserId);
        Assert.Equal("صندوق‌دار اول", result.Value?.DisplayName);
        Assert.Equal(UserRole.Cashier, result.Value?.Role);
    }

    [Fact]
    public async Task FailsWithAnUnknownUsernameUsingTheSameGenericMessageAsAWrongPassword()
    {
        var context = new ApplicationTestContext();
        context.Users.Items.Add(User.Create("cashier1", "صندوق‌دار اول", "PassWord123", UserRole.Cashier));

        var unknown = await Handler(context).ExecuteAsync(
            new SignInCommand("ghost", "PassWord123"), CancellationToken.None);
        var wrongPassword = await Handler(context).ExecuteAsync(
            new SignInCommand("cashier1", "WrongPassword1"), CancellationToken.None);

        Assert.False(unknown.IsSuccess);
        Assert.False(wrongPassword.IsSuccess);
        Assert.Equal(unknown.Error?.Code, wrongPassword.Error?.Code);
        Assert.Equal(unknown.Error?.Message, wrongPassword.Error?.Message);
    }

    [Fact]
    public async Task FailsForAnArchivedUserEvenWithTheRightPassword()
    {
        var context = new ApplicationTestContext();
        var user = User.Create("cashier1", "صندوق‌دار اول", "PassWord123", UserRole.Cashier);
        user.Archive();
        context.Users.Items.Add(user);

        var result = await Handler(context).ExecuteAsync(
            new SignInCommand("cashier1", "PassWord123"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("identity.sign-in.invalid-credentials", result.Error?.Code);
    }

    private static SignInHandler Handler(ApplicationTestContext context) => new(context.Users);
}

public sealed class VerifyAdminCredentialTests
{
    [Fact]
    public async Task SucceedsForAnAdminWithTheRightPassword()
    {
        var context = new ApplicationTestContext();
        var admin = User.Create("boss", "مدیر", "PassWord123", UserRole.Admin);
        context.Users.Items.Add(admin);

        var result = await Handler(context).ExecuteAsync(
            new VerifyAdminCredentialCommand("boss", "PassWord123"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(admin.Id, result.Value);
    }

    [Fact]
    public async Task FailsForACashierEvenWithTheRightPassword()
    {
        var context = new ApplicationTestContext();
        context.Users.Items.Add(User.Create("cashier1", "صندوق‌دار", "PassWord123", UserRole.Cashier));

        var result = await Handler(context).ExecuteAsync(
            new VerifyAdminCredentialCommand("cashier1", "PassWord123"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("identity.verify-admin.rejected", result.Error?.Code);
    }

    [Fact]
    public async Task FailsForAnAdminWithTheWrongPassword()
    {
        var context = new ApplicationTestContext();
        context.Users.Items.Add(User.Create("boss", "مدیر", "PassWord123", UserRole.Admin));

        var result = await Handler(context).ExecuteAsync(
            new VerifyAdminCredentialCommand("boss", "WrongPassword1"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("identity.verify-admin.rejected", result.Error?.Code);
    }

    private static VerifyAdminCredentialHandler Handler(ApplicationTestContext context) => new(context.Users);
}

public sealed class CreateUserTests
{
    [Fact]
    public async Task AnAdminCanCreateAnotherCashier()
    {
        var context = new ApplicationTestContext();
        var admin = User.Create("boss", "مدیر", "PassWord123", UserRole.Admin);
        context.Users.Items.Add(admin);

        var result = await Handler(context).ExecuteAsync(
            new CreateUserCommand(admin.Id, "cashier2", "صندوق‌دار دوم", "PassWord123", UserRole.Cashier),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, context.Users.Items.Count);
        Assert.Equal(1, context.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task ACashierCannotCreateAnyone()
    {
        var context = new ApplicationTestContext();
        var cashier = User.Create("cashier1", "صندوق‌دار", "PassWord123", UserRole.Cashier);
        context.Users.Items.Add(cashier);

        var result = await Handler(context).ExecuteAsync(
            new CreateUserCommand(cashier.Id, "cashier2", "صندوق‌دار دوم", "PassWord123", UserRole.Cashier),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("identity.create-user.forbidden", result.Error?.Code);
        Assert.Single(context.Users.Items);
    }

    [Fact]
    public async Task RefusesADuplicateUsername()
    {
        var context = new ApplicationTestContext();
        var admin = User.Create("boss", "مدیر", "PassWord123", UserRole.Admin);
        context.Users.Items.Add(admin);
        context.Users.Items.Add(User.Create("cashier2", "صندوق‌دار دوم", "PassWord123", UserRole.Cashier));

        var result = await Handler(context).ExecuteAsync(
            new CreateUserCommand(admin.Id, "Cashier2", "صندوق‌دار سوم", "PassWord123", UserRole.Cashier),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("identity.create-user.duplicate-username", result.Error?.Code);
        Assert.Equal(2, context.Users.Items.Count);
    }

    private static CreateUserHandler Handler(ApplicationTestContext context) => new(context.Users, context.UnitOfWork);
}
