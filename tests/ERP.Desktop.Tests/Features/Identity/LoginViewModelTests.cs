using ERP.Domain.Identity;
using ERP.Presentation.Features.Identity;

namespace ERP.Desktop.Tests.Features.Identity;

public sealed class LoginViewModelTests
{
    [Fact]
    public async Task TheRightUsernameAndPasswordSignIn()
    {
        var backend = new InMemoryIdentityBackend();
        backend.Users.Add(User.Create("cashier1", "صندوق‌دار اول", "PassWord123", UserRole.Cashier));
        var viewModel = new LoginViewModel(backend.SignIn) { Username = "cashier1", Password = "PassWord123" };

        await viewModel.SubmitCommand.ExecuteAsync(null);

        Assert.Null(viewModel.ErrorMessage);
        Assert.NotNull(viewModel.CompletedUser);
        Assert.Equal("صندوق‌دار اول", viewModel.CompletedUser!.DisplayName);
    }

    [Fact]
    public async Task AWrongPasswordShowsAGenericErrorAndLeavesCompletedUserNull()
    {
        var backend = new InMemoryIdentityBackend();
        backend.Users.Add(User.Create("cashier1", "صندوق‌دار اول", "PassWord123", UserRole.Cashier));
        var viewModel = new LoginViewModel(backend.SignIn) { Username = "cashier1", Password = "WrongPassword1" };

        await viewModel.SubmitCommand.ExecuteAsync(null);

        Assert.NotNull(viewModel.ErrorMessage);
        Assert.Null(viewModel.CompletedUser);
    }

    [Fact]
    public async Task AnArchivedUserCannotSignInEvenWithTheRightPassword()
    {
        var backend = new InMemoryIdentityBackend();
        var user = User.Create("cashier1", "صندوق‌دار اول", "PassWord123", UserRole.Cashier);
        user.Archive();
        backend.Users.Add(user);
        var viewModel = new LoginViewModel(backend.SignIn) { Username = "cashier1", Password = "PassWord123" };

        await viewModel.SubmitCommand.ExecuteAsync(null);

        Assert.NotNull(viewModel.ErrorMessage);
        Assert.Null(viewModel.CompletedUser);
    }
}
