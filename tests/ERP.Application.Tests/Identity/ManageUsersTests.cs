using ERP.Application.Identity;
using ERP.Application.Tests.TestDoubles;
using ERP.Domain.Identity;

namespace ERP.Application.Tests.Identity;

/// <summary>«کاربران»: a manager's page — every step re-checked against the acting user, and the store never left without a manager.</summary>
public sealed class ManageUsersTests
{
    private static (ApplicationTestContext Context, User Admin, User Cashier) WithAStore()
    {
        var context = new ApplicationTestContext();
        var admin = User.Create("boss", "مدیر", "PassWord123", UserRole.Admin);
        var cashier = User.Create("sara", "سارا", "PassWord123", UserRole.Cashier);
        context.Users.Items.Add(admin);
        context.Users.Items.Add(cashier);
        return (context, admin, cashier);
    }

    [Fact]
    public async Task AManagerSeesEveryoneWithTheirSwitches()
    {
        var (context, admin, cashier) = WithAStore();
        cashier.SetPermissions(CashierPermissions.ReturnsAndExchange);

        var rows = await new ListUsersHandler(context.Users).ExecuteAsync(new ListUsersQuery(admin.Id), CancellationToken.None);

        Assert.True(rows.IsSuccess);
        Assert.Equal(2, rows.Value!.Count);
        Assert.Equal(CashierPermissions.ReturnsAndExchange, rows.Value.Single(row => row.Id == cashier.Id).Permissions);
    }

    [Fact]
    public async Task ACashierCannotOpenTheUsersList()
    {
        var (context, _, cashier) = WithAStore();

        var rows = await new ListUsersHandler(context.Users).ExecuteAsync(new ListUsersQuery(cashier.Id), CancellationToken.None);

        Assert.Equal("identity.access.denied", rows.Error?.Code);
    }

    [Fact]
    public async Task TurningACashiersSwitchOffIsSavedAndAudited()
    {
        var (context, admin, cashier) = WithAStore();

        var result = await Update(context).ExecuteAsync(
            new UpdateUserCommand(admin.Id, cashier.Id, "سارا احمدی", UserRole.Cashier,
                CashierPermissions.All & ~CashierPermissions.ViewCostAndProfit),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal("سارا احمدی", cashier.DisplayName);
        Assert.False(cashier.Can(AccessRight.ViewCostAndProfit));
        Assert.True(cashier.Can(AccessRight.ReturnsAndExchange));
        var entry = Assert.Single(context.Audit.Entries);
        Assert.Equal("identity.user.updated", entry.Action);
        Assert.Equal(admin.Id.Value, entry.ActorUserId);
        Assert.Contains("permissions=All", entry.OldValue, StringComparison.Ordinal);
        Assert.DoesNotContain("ViewCostAndProfit", entry.NewValue, StringComparison.Ordinal);
        Assert.Equal(1, context.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task ACashierCannotChangeAnyone()
    {
        var (context, admin, cashier) = WithAStore();

        var result = await Update(context).ExecuteAsync(
            new UpdateUserCommand(cashier.Id, cashier.Id, "سارا", UserRole.Admin, CashierPermissions.All),
            CancellationToken.None);

        Assert.Equal("identity.access.denied", result.Error?.Code);
        Assert.Equal(UserRole.Cashier, cashier.Role);
        Assert.Equal(UserRole.Admin, admin.Role);
        Assert.Empty(context.Audit.Entries);
    }

    [Fact]
    public async Task TheLastManagerCannotBeDemotedOrArchived()
    {
        var (context, admin, _) = WithAStore();
        var other = User.Create("boss2", "مدیر دوم", "PassWord123", UserRole.Admin);
        other.Archive();
        context.Users.Items.Add(other);

        var demoted = await Update(context).ExecuteAsync(
            new UpdateUserCommand(admin.Id, admin.Id, "مدیر", UserRole.Cashier, CashierPermissions.All), CancellationToken.None);

        Assert.Equal("identity.user.last-admin", demoted.Error?.Code);
        Assert.Equal(UserRole.Admin, admin.Role);
    }

    [Fact]
    public async Task WithASecondManagerOneCanStepDown()
    {
        var (context, admin, _) = WithAStore();
        var other = User.Create("boss2", "مدیر دوم", "PassWord123", UserRole.Admin);
        context.Users.Items.Add(other);

        var demoted = await Update(context).ExecuteAsync(
            new UpdateUserCommand(admin.Id, other.Id, "مدیر دوم", UserRole.Cashier, CashierPermissions.None), CancellationToken.None);

        Assert.True(demoted.IsSuccess, demoted.Error?.Message);
        Assert.False(other.Can(AccessRight.ManageUsers));
        Assert.False(other.Can(AccessRight.ReturnsAndExchange));
        Assert.True(other.Can(AccessRight.SellAndTill));
    }

    [Fact]
    public async Task AResetPasswordWorksAtTheNextSignInAndIsNeverWrittenToTheAudit()
    {
        var (context, admin, cashier) = WithAStore();

        var result = await new ResetUserPasswordHandler(context.Users, context.Audit, context.UnitOfWork, context.Clock)
            .ExecuteAsync(new ResetUserPasswordCommand(admin.Id, cashier.Id, "NewPass4567"), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.True(cashier.VerifyPassword("NewPass4567"));
        Assert.False(cashier.VerifyPassword("PassWord123"));
        var entry = Assert.Single(context.Audit.Entries);
        Assert.Equal("identity.user.password-reset", entry.Action);
        Assert.Null(entry.OldValue);
        Assert.Null(entry.NewValue);
    }

    [Fact]
    public async Task AnArchivedCashierCanNoLongerSignInOrDoAnything()
    {
        var (context, admin, cashier) = WithAStore();

        var result = await Archive(context).ExecuteAsync(new ArchiveUserCommand(admin.Id, cashier.Id), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.False(cashier.VerifyPassword("PassWord123"));
        Assert.False(cashier.Can(AccessRight.SellAndTill));
        Assert.Equal("identity.user.archived", Assert.Single(context.Audit.Entries).Action);
    }

    [Fact]
    public async Task NobodyArchivesThemselves()
    {
        var (context, admin, _) = WithAStore();
        context.Users.Items.Add(User.Create("boss2", "مدیر دوم", "PassWord123", UserRole.Admin));

        var result = await Archive(context).ExecuteAsync(new ArchiveUserCommand(admin.Id, admin.Id), CancellationToken.None);

        Assert.Equal("identity.user.self", result.Error?.Code);
        Assert.Equal(UserStatus.Active, admin.Status);
    }

    [Fact]
    public async Task ARightTakenAwayIsGoneOnTheNextCheckNotTheNextSignIn()
    {
        var (context, admin, cashier) = WithAStore();
        var policy = new AccessPolicy(context.Users);
        Assert.Null(await policy.CheckAsync(cashier.Id.Value, AccessRight.CorrectPostedInvoices, CancellationToken.None));

        await Update(context).ExecuteAsync(
            new UpdateUserCommand(admin.Id, cashier.Id, "سارا", UserRole.Cashier, CashierPermissions.ReturnsAndExchange),
            CancellationToken.None);

        var denied = await policy.CheckAsync(cashier.Id.Value, AccessRight.CorrectPostedInvoices, CancellationToken.None);
        Assert.Equal("identity.access.denied", denied?.Code);
        Assert.Contains("اصلاح فاکتور", denied?.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnknownActingUserIsRefused()
    {
        var (context, _, _) = WithAStore();

        var denied = await new AccessPolicy(context.Users).CheckAsync(Guid.NewGuid(), AccessRight.SellAndTill, CancellationToken.None);

        Assert.NotNull(denied);
    }

    [Fact]
    public async Task ANewCashierCanBeCreatedWithSomeSwitchesOff()
    {
        var (context, admin, _) = WithAStore();

        var created = await new CreateUserHandler(context.Users, context.Audit, context.UnitOfWork, context.Clock).ExecuteAsync(
            new CreateUserCommand(admin.Id, "reza", "رضا", "PassWord123", UserRole.Cashier, CashierPermissions.EditCustomers),
            CancellationToken.None);

        Assert.True(created.IsSuccess, created.Error?.Message);
        var reza = context.Users.Items.Single(user => user.Id == created.Value);
        Assert.True(reza.Can(AccessRight.EditCustomers));
        Assert.False(reza.Can(AccessRight.ReturnsAndExchange));
        Assert.Equal("identity.user.created", Assert.Single(context.Audit.Entries).Action);
    }

    private static UpdateUserHandler Update(ApplicationTestContext context) =>
        new(context.Users, context.Audit, context.UnitOfWork, context.Clock);

    private static ArchiveUserHandler Archive(ApplicationTestContext context) =>
        new(context.Users, context.Audit, context.UnitOfWork, context.Clock);
}

public sealed class AccessRulesTests
{
    [Theory]
    [InlineData(AccessRight.ManageCatalog)]
    [InlineData(AccessRight.ManageStock)]
    [InlineData(AccessRight.ManageBackups)]
    [InlineData(AccessRight.ManageUsers)]
    public void AManagersOnlyRightIsNeverACashiersEvenWithEverySwitchOn(AccessRight right)
    {
        Assert.False(AccessRules.Allows(UserRole.Cashier, CashierPermissions.All, right));
        Assert.True(AccessRules.Allows(UserRole.Admin, CashierPermissions.None, right));
    }

    [Theory]
    [InlineData(AccessRight.ReturnsAndExchange, CashierPermissions.ReturnsAndExchange)]
    [InlineData(AccessRight.CorrectPostedInvoices, CashierPermissions.CorrectPostedInvoices)]
    [InlineData(AccessRight.ViewCostAndProfit, CashierPermissions.ViewCostAndProfit)]
    [InlineData(AccessRight.EditCustomers, CashierPermissions.EditCustomers)]
    public void EachSwitchGivesExactlyItsOwnRight(AccessRight right, CashierPermissions permission)
    {
        Assert.True(AccessRules.Allows(UserRole.Cashier, permission, right));
        Assert.False(AccessRules.Allows(UserRole.Cashier, CashierPermissions.All & ~permission, right));
    }

    [Fact]
    public void SellingNeedsNoSwitch() =>
        Assert.True(AccessRules.Allows(UserRole.Cashier, CashierPermissions.None, AccessRight.SellAndTill));
}
