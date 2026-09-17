using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ERP.Application.Identity;

namespace ERP.Presentation.Features.Identity;

/// <summary>Every launch after the first — no shared logins (§3, §15.3): each person types their own username and password.</summary>
public sealed partial class LoginViewModel : ObservableObject
{
    private readonly ISignInHandler _signIn;

    public LoginViewModel(ISignInHandler signIn)
    {
        _signIn = signIn;
    }

    [ObservableProperty]
    public partial string Username { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Password { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>Set once <see cref="SubmitAsync"/> succeeds.</summary>
    public SignedInUser? CompletedUser { get; private set; }

    /// <summary>Raised right after <see cref="CompletedUser"/> is set, for a caller (the auth window) that needs to react rather than poll.</summary>
    public event Action<SignedInUser>? Succeeded;

    [RelayCommand]
    private async Task SubmitAsync()
    {
        ErrorMessage = null;
        IsBusy = true;
        try
        {
            var result = await _signIn.ExecuteAsync(new SignInCommand(Username, Password), CancellationToken.None);
            if (!result.IsSuccess)
            {
                ErrorMessage = result.Error?.Message ?? "ورود ناموفق بود.";
                return;
            }

            CompletedUser = result.Value;
            Succeeded?.Invoke(result.Value!);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
