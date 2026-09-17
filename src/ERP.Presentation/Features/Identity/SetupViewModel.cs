using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ERP.Application.Identity;
using ERP.Domain.Identity;

namespace ERP.Presentation.Features.Identity;

/// <summary>
/// The very first screen a fresh install shows — «مدیر اولیه در راه‌اندازی
/// ساخته می‌شود» (milestone-1 §10). Reachable only once: the caller only
/// shows this page when <see cref="IRegisterFirstAdminHandler"/>'s own
/// already-exists check would otherwise still catch a race.
/// </summary>
public sealed partial class SetupViewModel : ObservableObject
{
    private readonly IRegisterFirstAdminHandler _registerFirstAdmin;

    public SetupViewModel(IRegisterFirstAdminHandler registerFirstAdmin)
    {
        _registerFirstAdmin = registerFirstAdmin;
    }

    [ObservableProperty]
    public partial string Username { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DisplayName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Password { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ConfirmPassword { get; set; } = string.Empty;

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

        if (Password != ConfirmPassword)
        {
            ErrorMessage = "تکرار رمز عبور با رمز عبور یکسان نیست.";
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _registerFirstAdmin.ExecuteAsync(
                new RegisterFirstAdminCommand(Username, DisplayName, Password), CancellationToken.None);
            if (!result.IsSuccess)
            {
                ErrorMessage = result.Error?.Message ?? "ساخت حساب مدیر ناموفق بود.";
                return;
            }

            var signedInUser = new SignedInUser(result.Value, DisplayName.Trim(), UserRole.Admin);
            CompletedUser = signedInUser;
            Succeeded?.Invoke(signedInUser);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
