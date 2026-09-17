using ERP.Domain.Identity;
using ERP.Presentation.Features.Identity;

namespace ERP.Desktop.Tests.Features.Identity;

public sealed class SetupViewModelTests
{
    [Fact]
    public async Task CreatingTheFirstAdminSucceedsAndReportsTheAdminRole()
    {
        var backend = new InMemoryIdentityBackend();
        var viewModel = new SetupViewModel(backend.RegisterFirstAdmin)
        {
            Username = "owner",
            DisplayName = "صاحب فروشگاه",
            Password = "PassWord123",
            ConfirmPassword = "PassWord123",
        };

        await viewModel.SubmitCommand.ExecuteAsync(null);

        Assert.Null(viewModel.ErrorMessage);
        Assert.NotNull(viewModel.CompletedUser);
        Assert.Equal(UserRole.Admin, viewModel.CompletedUser!.Role);
        Assert.Single(backend.Users);
    }

    [Fact]
    public async Task AMismatchedConfirmationIsRejectedWithoutTouchingTheBackend()
    {
        var backend = new InMemoryIdentityBackend();
        var viewModel = new SetupViewModel(backend.RegisterFirstAdmin)
        {
            Username = "owner",
            DisplayName = "صاحب فروشگاه",
            Password = "PassWord123",
            ConfirmPassword = "SomethingElse1",
        };

        await viewModel.SubmitCommand.ExecuteAsync(null);

        Assert.NotNull(viewModel.ErrorMessage);
        Assert.Null(viewModel.CompletedUser);
        Assert.Empty(backend.Users);
    }

    [Fact]
    public async Task ATooShortPasswordShowsTheDomainMessageAndLeavesCompletedUserNull()
    {
        var backend = new InMemoryIdentityBackend();
        var viewModel = new SetupViewModel(backend.RegisterFirstAdmin)
        {
            Username = "owner",
            DisplayName = "صاحب فروشگاه",
            Password = "short",
            ConfirmPassword = "short",
        };

        await viewModel.SubmitCommand.ExecuteAsync(null);

        Assert.NotNull(viewModel.ErrorMessage);
        Assert.Null(viewModel.CompletedUser);
    }

    [Fact]
    public async Task ASecondSetupAttemptIsRefusedOnceAnAdminAlreadyExists()
    {
        var backend = new InMemoryIdentityBackend();
        backend.Users.Add(User.Create("owner", "اولی", "PassWord123", UserRole.Admin));
        var viewModel = new SetupViewModel(backend.RegisterFirstAdmin)
        {
            Username = "intruder",
            DisplayName = "کاربر دیگر",
            Password = "PassWord123",
            ConfirmPassword = "PassWord123",
        };

        await viewModel.SubmitCommand.ExecuteAsync(null);

        Assert.NotNull(viewModel.ErrorMessage);
        Assert.Null(viewModel.CompletedUser);
        Assert.Single(backend.Users);
    }
}
