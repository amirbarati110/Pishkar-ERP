using ERP.Domain.Identity;
using ERP.Presentation.Features.Identity;

namespace ERP.Desktop.Tests.Features.Identity;

public sealed class UsersViewModelTests
{
    private static (InMemoryIdentityBackend Backend, User Admin, UsersViewModel ViewModel) Create()
    {
        var backend = new InMemoryIdentityBackend();
        var admin = User.Create("boss", "مدیر", "PassWord123", UserRole.Admin);
        backend.Users.Add(admin);
        var viewModel = new UsersViewModel(
            admin.Id, backend.ListUsers, backend.CreateUser, backend.UpdateUser, backend.ResetPassword, backend.ArchiveUser);
        return (backend, admin, viewModel);
    }

    [Fact]
    public async Task LoadingListsEveryoneWithRoleAndStatus()
    {
        var (backend, _, viewModel) = Create();
        var cashier = User.Create("sara", "سارا", "PassWord123", UserRole.Cashier);
        cashier.SetPermissions(CashierPermissions.ReturnsAndExchange);
        backend.Users.Add(cashier);

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.Equal(2, viewModel.Items.Count);
        var row = viewModel.Items.Single(item => item.Row.Id == cashier.Id);
        Assert.Equal("صندوق‌دار", row.RoleText);
        Assert.Equal("فعال", row.StatusText);
    }

    [Fact]
    public async Task ANewCashierIsCreatedWithTheCheckedSwitchesOnly()
    {
        var (backend, _, viewModel) = Create();
        viewModel.NewCommand.Execute(null);
        viewModel.DisplayName = "سارا احمدی";
        viewModel.Username = "sara";
        viewModel.Password = "NewPass4567";
        viewModel.AllowViewCost = false;

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsEditorOpen);
        var created = backend.Users.Single(user => user.Username == "sara");
        Assert.True(created.Can(AccessRight.ReturnsAndExchange));
        Assert.False(created.Can(AccessRight.ViewCostAndProfit));
        Assert.Equal("«سارا احمدی» ساخته شد.", viewModel.Notice);
    }

    [Fact]
    public async Task AShortPasswordIsCaughtBeforeAskingTheServer()
    {
        var (backend, _, viewModel) = Create();
        viewModel.NewCommand.Execute(null);
        viewModel.DisplayName = "سارا";
        viewModel.Username = "sara";
        viewModel.Password = "short";

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal("رمز عبور باید حداقل ۸ نویسه باشد.", viewModel.PasswordError);
        Assert.True(viewModel.IsEditorOpen);
        Assert.DoesNotContain(backend.Users, user => user.Username == "sara");
    }

    [Fact]
    public async Task EditingTurnsASwitchOffAndTheFormNeverShowsSwitchesForAManager()
    {
        var (backend, _, viewModel) = Create();
        var cashier = User.Create("sara", "سارا", "PassWord123", UserRole.Cashier);
        backend.Users.Add(cashier);
        await viewModel.LoadAsync(CancellationToken.None);
        viewModel.SelectedItem = viewModel.Items.Single(item => item.Row.Id == cashier.Id);

        viewModel.EditCommand.Execute(null);
        Assert.False(viewModel.IsUsernameEditable);
        Assert.True(viewModel.SwitchesApply);
        viewModel.AllowCorrections = false;
        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.False(cashier.Can(AccessRight.CorrectPostedInvoices));
        Assert.True(cashier.Can(AccessRight.ReturnsAndExchange));

        viewModel.SelectedItem = viewModel.Items.Single(item => item.Row.Id == cashier.Id);
        viewModel.EditCommand.Execute(null);
        viewModel.IsAdmin = true;
        Assert.False(viewModel.SwitchesApply);
    }

    [Fact]
    public async Task ResettingAPasswordWorksAtTheNextSignIn()
    {
        var (backend, _, viewModel) = Create();
        var cashier = User.Create("sara", "سارا", "PassWord123", UserRole.Cashier);
        backend.Users.Add(cashier);
        await viewModel.LoadAsync(CancellationToken.None);
        viewModel.SelectedItem = viewModel.Items.Single(item => item.Row.Id == cashier.Id);

        viewModel.ResetPasswordCommand.Execute(null);
        Assert.True(viewModel.IsResetPasswordOpen);
        viewModel.NewPassword = "NewPass4567";
        await viewModel.ConfirmResetPasswordCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsResetPasswordOpen);
        Assert.True(cashier.VerifyPassword("NewPass4567"));
        Assert.False(cashier.VerifyPassword("PassWord123"));
    }

    [Fact]
    public async Task ArchivingAsksFirstAndThenTakesTheUserOutOfUse()
    {
        var (backend, _, viewModel) = Create();
        var cashier = User.Create("sara", "سارا", "PassWord123", UserRole.Cashier);
        backend.Users.Add(cashier);
        await viewModel.LoadAsync(CancellationToken.None);
        viewModel.SelectedItem = viewModel.Items.Single(item => item.Row.Id == cashier.Id);

        viewModel.ArchiveCommand.Execute(null);
        Assert.True(viewModel.IsArchiveConfirmOpen);
        await viewModel.ConfirmArchiveCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsArchiveConfirmOpen);
        Assert.Equal(UserStatus.Archived, cashier.Status);
        Assert.Null(viewModel.SelectedItem);
    }

    [Fact]
    public async Task TheLastActiveManagerIsRefusedWithTheServersMessage()
    {
        var (_, admin, viewModel) = Create();
        await viewModel.LoadAsync(CancellationToken.None);
        viewModel.SelectedItem = viewModel.Items.Single(item => item.Row.Id == admin.Id);

        viewModel.EditCommand.Execute(null);
        viewModel.IsAdmin = false;
        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsEditorOpen);
        Assert.False(string.IsNullOrEmpty(viewModel.EditorError));
        Assert.Equal(UserRole.Admin, admin.Role);
    }
}
