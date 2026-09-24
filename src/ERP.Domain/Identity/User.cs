using ERP.Domain.Common;

namespace ERP.Domain.Identity;

/// <summary>
/// Someone who can sign in to the till. Milestone-1 build order step 4
/// («Setup، Identity، Permission، Settings و Audit») — until this existed,
/// every "manager approval" and every Audit row's actor was a hard-coded
/// placeholder GUID, not a real person (checklist appendix د.۲).
/// </summary>
public sealed class User : Entity<UserId>
{
    private const int MaximumUsernameLength = 40;
    private const int MinimumUsernameLength = 3;
    private const int MaximumDisplayNameLength = 80;

    private User(UserId id, string username, string displayName, PasswordHash passwordHash, UserRole role)
        : base(id)
    {
        Username = username;
        DisplayName = displayName;
        PasswordHash = passwordHash;
        Role = role;
        Status = UserStatus.Active;
        Permissions = CashierPermissions.All;
    }

    /// <summary>The login identifier — Latin/ASCII on purpose (typed at every login; a Persian display name is separate, see <see cref="DisplayName"/>).</summary>
    public string Username { get; private set; }

    /// <summary>«کاربر آزمایشی»-style name shown on screen — the Persian name people actually go by.</summary>
    public string DisplayName { get; private set; }

    public PasswordHash PasswordHash { get; private set; }

    public UserRole Role { get; private set; }

    public UserStatus Status { get; private set; }

    /// <summary>What a manager allowed this person beyond the till — only a cashier's are ever consulted; a manager holds every right.</summary>
    public CashierPermissions Permissions { get; private set; }

    public bool Can(AccessRight right) => Status == UserStatus.Active && AccessRules.Allows(Role, Permissions, right);

    public static User Create(string username, string displayName, string plainPassword, UserRole role)
    {
        return new User(UserId.New(), NormalizeUsername(username), NormalizeDisplayName(displayName), PasswordHash.Create(plainPassword), role);
    }

    internal static User Rehydrate(
        UserId id,
        string username,
        string displayName,
        PasswordHash passwordHash,
        UserRole role,
        UserStatus status,
        CashierPermissions permissions = CashierPermissions.All)
    {
        return new User(id, username, displayName, passwordHash, role)
        {
            Status = status,
            Permissions = permissions,
        };
    }

    /// <summary>An archived user's password never matches — so a disabled account cannot sign back in even with the right password.</summary>
    public bool VerifyPassword(string? plainPassword) => Status == UserStatus.Active && PasswordHash.Matches(plainPassword);

    public void ChangePassword(string newPlainPassword) => PasswordHash = PasswordHash.Create(newPlainPassword);

    public void Rename(string displayName) => DisplayName = NormalizeDisplayName(displayName);

    public void ChangeRole(UserRole role)
    {
        if (!Enum.IsDefined(role))
        {
            throw new DomainException("نقش کاربر معتبر نیست.");
        }

        Role = role;
    }

    public void SetPermissions(CashierPermissions permissions) => Permissions = permissions & CashierPermissions.All;

    public void Archive() => Status = UserStatus.Archived;

    public void Reactivate() => Status = UserStatus.Active;

    private static string NormalizeUsername(string username)
    {
        var trimmed = (username ?? string.Empty).Trim();
        if (trimmed.Length is < MinimumUsernameLength or > MaximumUsernameLength)
        {
            throw new DomainException($"نام کاربری باید بین {MinimumUsernameLength} تا {MaximumUsernameLength} نویسه باشد.");
        }

        if (!trimmed.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-'))
        {
            throw new DomainException("نام کاربری فقط می‌تواند حروف و رقم انگلیسی، نقطه، خط‌تیره و زیرخط داشته باشد.");
        }

        return trimmed.ToLowerInvariant();
    }

    private static string NormalizeDisplayName(string displayName)
    {
        var trimmed = (displayName ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            throw new DomainException("نام نمایشی کاربر را وارد کنید.");
        }

        if (trimmed.Length > MaximumDisplayNameLength)
        {
            throw new DomainException($"نام نمایشی نمی‌تواند بیشتر از {MaximumDisplayNameLength} نویسه باشد.");
        }

        return trimmed;
    }
}
