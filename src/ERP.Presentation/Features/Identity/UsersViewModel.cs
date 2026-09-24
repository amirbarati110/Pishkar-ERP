using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ERP.Application.Identity;
using ERP.Domain.Identity;

namespace ERP.Presentation.Features.Identity;

/// <summary>One row of «کاربران» as the screen writes it.</summary>
public sealed class UserListItem
{
    public UserListItem(UserListRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        Row = row;
        DisplayName = row.DisplayName;
        Username = row.Username;
        RoleText = row.Role == UserRole.Admin ? "مدیر" : "صندوق‌دار";
        StatusText = row.Status == UserStatus.Active ? "فعال" : "غیرفعال";
    }

    public UserListRow Row { get; }

    public string DisplayName { get; }

    public string Username { get; }

    public string RoleText { get; }

    public string StatusText { get; }

    public bool IsArchived => Row.Status == UserStatus.Archived;

    public string AccessibleName => $"{DisplayName}، {RoleText}، {StatusText}";
}

/// <summary>
/// «کاربران» (appendix ز.۲): a manager's own screen to see everyone, change a name, role or a
/// cashier's four switches, set a new password, and take someone out of use. Reachable only when
/// <see cref="AccessRight.ManageUsers"/> is held — the page itself never decides that, the caller
/// does, the same as every other manager-only screen (§10).
/// </summary>
public sealed partial class UsersViewModel : ObservableObject
{
    private readonly UserId _actingUserId;
    private readonly IListUsersHandler _list;
    private readonly ICreateUserHandler _create;
    private readonly IUpdateUserHandler _update;
    private readonly IResetUserPasswordHandler _resetPassword;
    private readonly IArchiveUserHandler _archive;
    private UserListRow? _editing;

    public UsersViewModel(
        UserId actingUserId,
        IListUsersHandler list,
        ICreateUserHandler create,
        IUpdateUserHandler update,
        IResetUserPasswordHandler resetPassword,
        IArchiveUserHandler archive)
    {
        _actingUserId = actingUserId;
        _list = list;
        _create = create;
        _update = update;
        _resetPassword = resetPassword;
        _archive = archive;
    }

    /// <summary>Where an unexpected failure is written (the app's log). Null only in tests, where it should surface.</summary>
    public Action<Exception>? ReportUnexpectedError { get; set; }

    public ObservableCollection<UserListItem> Items { get; } = [];

    [ObservableProperty]
    public partial UserListItem? SelectedItem { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial string? Notice { get; set; }

    // ───── the form over the list ─────

    [ObservableProperty]
    public partial bool IsEditorOpen { get; set; }

    [ObservableProperty]
    public partial string EditorTitle { get; set; } = string.Empty;

    /// <summary>Only on «کاربر جدید» — a username can never change once the person has signed in with it.</summary>
    [ObservableProperty]
    public partial bool IsUsernameEditable { get; set; }

    [ObservableProperty]
    public partial string Username { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DisplayName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Password { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsAdmin { get; set; }

    [ObservableProperty]
    public partial bool AllowReturns { get; set; } = true;

    [ObservableProperty]
    public partial bool AllowCorrections { get; set; } = true;

    [ObservableProperty]
    public partial bool AllowViewCost { get; set; } = true;

    [ObservableProperty]
    public partial bool AllowEditCustomers { get; set; } = true;

    /// <summary>The four switches only mean anything for a cashier — a manager already holds every right.</summary>
    public bool SwitchesApply => !IsAdmin;

    [ObservableProperty]
    public partial string? UsernameError { get; set; }

    [ObservableProperty]
    public partial string? DisplayNameError { get; set; }

    [ObservableProperty]
    public partial string? PasswordError { get; set; }

    [ObservableProperty]
    public partial string? EditorError { get; set; }

    [ObservableProperty]
    public partial bool IsSaving { get; set; }

    // ───── «حذف» asks first ─────

    [ObservableProperty]
    public partial bool IsArchiveConfirmOpen { get; set; }

    [ObservableProperty]
    public partial string ArchiveConfirmText { get; set; } = string.Empty;

    public bool HasSelection => SelectedItem is not null;

    partial void OnSelectedItemChanged(UserListItem? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        EditCommand.NotifyCanExecuteChanged();
        ArchiveCommand.NotifyCanExecuteChanged();
        ResetPasswordCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsAdminChanged(bool value) => OnPropertyChanged(nameof(SwitchesApply));

    /// <summary>First open.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RefreshAsync(cancellationToken);
        }
        catch (Exception exception) when (ReportUnexpectedError is not null)
        {
            ReportUnexpectedError(exception);
            ErrorMessage = "لیست کاربران خوانده نشد و خطا برای پشتیبانی ثبت شد. دوباره امتحان کنید.";
        }
    }

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var keep = SelectedItem?.Row.Id;
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var result = await _list.ExecuteAsync(new ListUsersQuery(_actingUserId), cancellationToken);
            if (!result.IsSuccess)
            {
                ErrorMessage = result.Error?.Message;
                return;
            }

            Items.Clear();
            foreach (var row in result.Value!)
            {
                Items.Add(new UserListItem(row));
            }

            SelectedItem = Items.FirstOrDefault(item => item.Row.Id == keep);
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ───── new / edit ─────

    /// <summary>«جدید (F2)».</summary>
    [RelayCommand]
    private void New()
    {
        _editing = null;
        OpenEditor("کاربر جدید", isUsernameEditable: true, string.Empty, string.Empty, UserRole.Cashier, CashierPermissions.All);
    }

    /// <summary>«ویرایش (F3)» on the selected user.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Edit()
    {
        if (SelectedItem is not { } item)
        {
            return;
        }

        _editing = item.Row;
        OpenEditor("ویرایش کاربر", isUsernameEditable: false, item.Row.Username, item.Row.DisplayName, item.Row.Role, item.Row.Permissions);
    }

    private void OpenEditor(string title, bool isUsernameEditable, string username, string displayName, UserRole role, CashierPermissions permissions)
    {
        EditorTitle = title;
        IsUsernameEditable = isUsernameEditable;
        Username = username;
        DisplayName = displayName;
        Password = string.Empty;
        IsAdmin = role == UserRole.Admin;
        AllowReturns = permissions.HasFlag(CashierPermissions.ReturnsAndExchange);
        AllowCorrections = permissions.HasFlag(CashierPermissions.CorrectPostedInvoices);
        AllowViewCost = permissions.HasFlag(CashierPermissions.ViewCostAndProfit);
        AllowEditCustomers = permissions.HasFlag(CashierPermissions.EditCustomers);
        UsernameError = null;
        DisplayNameError = null;
        PasswordError = null;
        EditorError = null;
        Notice = null;
        IsEditorOpen = true;
    }

    [RelayCommand]
    private void CancelEditor() => IsEditorOpen = false;

    private CashierPermissions SelectedPermissions()
    {
        var permissions = CashierPermissions.None;
        if (AllowReturns)
        {
            permissions |= CashierPermissions.ReturnsAndExchange;
        }

        if (AllowCorrections)
        {
            permissions |= CashierPermissions.CorrectPostedInvoices;
        }

        if (AllowViewCost)
        {
            permissions |= CashierPermissions.ViewCostAndProfit;
        }

        if (AllowEditCustomers)
        {
            permissions |= CashierPermissions.EditCustomers;
        }

        return permissions;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        UsernameError = null;
        DisplayNameError = null;
        PasswordError = null;
        EditorError = null;

        if (string.IsNullOrWhiteSpace(DisplayName))
        {
            DisplayNameError = "نام کاربر را وارد کنید.";
            return;
        }

        if (_editing is null && string.IsNullOrWhiteSpace(Username))
        {
            UsernameError = "نام کاربری را وارد کنید.";
            return;
        }

        if (_editing is null && Password.Length < 8)
        {
            PasswordError = "رمز عبور باید حداقل ۸ نویسه باشد.";
            return;
        }

        var role = IsAdmin ? UserRole.Admin : UserRole.Cashier;
        var permissions = SelectedPermissions();

        IsSaving = true;
        try
        {
            if (_editing is { } editing)
            {
                var updated = await _update.ExecuteAsync(
                    new UpdateUserCommand(_actingUserId, editing.Id, DisplayName.Trim(), role, permissions), CancellationToken.None);
                if (!updated.IsSuccess)
                {
                    ShowSaveError(updated.Error?.Code, updated.Error?.Message);
                    return;
                }

                Notice = $"«{DisplayName.Trim()}» ذخیره شد.";
            }
            else
            {
                var created = await _create.ExecuteAsync(
                    new CreateUserCommand(_actingUserId, Username.Trim(), DisplayName.Trim(), Password, role, permissions),
                    CancellationToken.None);
                if (!created.IsSuccess)
                {
                    ShowSaveError(created.Error?.Code, created.Error?.Message);
                    return;
                }

                Notice = $"«{DisplayName.Trim()}» ساخته شد.";
            }

            IsEditorOpen = false;
            await RefreshAsync(CancellationToken.None);
        }
        catch (Exception exception) when (ReportUnexpectedError is not null)
        {
            ReportUnexpectedError(exception);
            EditorError = "ذخیره انجام نشد و خطا برای پشتیبانی ثبت شد. دوباره امتحان کنید.";
        }
        finally
        {
            IsSaving = false;
        }
    }

    private void ShowSaveError(string? code, string? message)
    {
        if (code == "identity.create-user.duplicate-username")
        {
            UsernameError = message;
        }
        else if (code == "identity.user.last-admin")
        {
            EditorError = message;
        }
        else
        {
            EditorError = message;
        }
    }

    // ───── رمز تازه ─────

    [ObservableProperty]
    public partial bool IsResetPasswordOpen { get; set; }

    [ObservableProperty]
    public partial string NewPassword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? ResetPasswordError { get; set; }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void ResetPassword()
    {
        NewPassword = string.Empty;
        ResetPasswordError = null;
        Notice = null;
        IsResetPasswordOpen = true;
    }

    [RelayCommand]
    private void CancelResetPassword() => IsResetPasswordOpen = false;

    [RelayCommand]
    private async Task ConfirmResetPasswordAsync()
    {
        if (SelectedItem is not { } item)
        {
            return;
        }

        if (NewPassword.Length < 8)
        {
            ResetPasswordError = "رمز عبور باید حداقل ۸ نویسه باشد.";
            return;
        }

        try
        {
            var result = await _resetPassword.ExecuteAsync(
                new ResetUserPasswordCommand(_actingUserId, item.Row.Id, NewPassword), CancellationToken.None);
            if (result.IsSuccess)
            {
                IsResetPasswordOpen = false;
                Notice = $"رمز عبور «{item.DisplayName}» عوض شد.";
            }
            else
            {
                ResetPasswordError = result.Error?.Message;
            }
        }
        catch (Exception exception) when (ReportUnexpectedError is not null)
        {
            ReportUnexpectedError(exception);
            ResetPasswordError = "عوض کردن رمز انجام نشد و خطا برای پشتیبانی ثبت شد. دوباره امتحان کنید.";
        }
    }

    // ───── delete = archive ─────

    /// <summary>«حذف (F4)»: asks first. Nothing is archived until <see cref="ConfirmArchiveCommand"/>.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Archive()
    {
        if (SelectedItem is not { } item)
        {
            return;
        }

        ArchiveConfirmText = $"«{item.DisplayName}» دیگر نتواند وارد برنامه شود؟ نامش روی فاکتورها و گزارش‌های قبلی می‌ماند.";
        IsArchiveConfirmOpen = true;
    }

    [RelayCommand]
    private void CancelArchive() => IsArchiveConfirmOpen = false;

    [RelayCommand]
    private async Task ConfirmArchiveAsync()
    {
        IsArchiveConfirmOpen = false;
        if (SelectedItem is not { } item)
        {
            return;
        }

        try
        {
            var result = await _archive.ExecuteAsync(new ArchiveUserCommand(_actingUserId, item.Row.Id), CancellationToken.None);
            if (result.IsSuccess)
            {
                Notice = $"«{item.DisplayName}» غیرفعال شد.";
                SelectedItem = null;
                await RefreshAsync(CancellationToken.None);
            }
            else
            {
                ErrorMessage = result.Error?.Message;
            }
        }
        catch (Exception exception) when (ReportUnexpectedError is not null)
        {
            ReportUnexpectedError(exception);
            ErrorMessage = "غیرفعال کردن انجام نشد و خطا برای پشتیبانی ثبت شد. دوباره امتحان کنید.";
        }
    }
}
