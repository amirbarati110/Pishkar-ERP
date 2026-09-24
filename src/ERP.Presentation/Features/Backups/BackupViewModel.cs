using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ERP.Application.Backups;
using ERP.Domain.Backups;
using ERP.Domain.Common;
using ERP.Presentation.Features.Sales;

namespace ERP.Presentation.Features.Backups;

/// <summary>
/// «Backup محلی قابل اعتبارسنجی» + «سلامت سیستم» (milestone-1 §14/§15
/// acceptance criterion 11, build order step 11). One page, three plain
/// facts and two buttons — not a diagnostics dashboard: is the database
/// reachable, are its migrations current, and how healthy is the most
/// recent backup.
/// </summary>
public sealed partial class BackupViewModel : ObservableObject
{
    private readonly ICreateBackupHandler _create;
    private readonly IVerifyBackupHandler _verify;
    private readonly IGetSystemHealthHandler _health;
    private readonly IRestoreBackupHandler _restore;
    private readonly string _backupDirectory;

    private BackupRecordId? _latestBackupId;
    private string? _latestBackupFilePath;
    private string? _restoreFilePath;

    public BackupViewModel(
        ICreateBackupHandler create,
        IVerifyBackupHandler verify,
        IGetSystemHealthHandler health,
        IRestoreBackupHandler restore,
        string backupDirectory)
    {
        _create = create;
        _verify = verify;
        _health = health;
        _restore = restore;
        _backupDirectory = backupDirectory;
    }

    /// <summary>The folder the app writes backups to — shown so a person knows where to find them (and to copy them to a USB disk).</summary>
    public string BackupDirectory => _backupDirectory;

    // ───── «بازیابی» (§15.10–15.12): two confirmations — a warning, then a manager's own password ─────

    [ObservableProperty]
    public partial bool CanRestoreLatest { get; set; }

    /// <summary>Step 1: what will happen, in plain words.</summary>
    [ObservableProperty]
    public partial bool IsRestoreWarningOpen { get; set; }

    /// <summary>Step 2: a manager's username and password — nothing is replaced before this.</summary>
    [ObservableProperty]
    public partial bool IsRestoreCredentialsOpen { get; set; }

    [ObservableProperty]
    public partial string RestoreWarningText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string AdminUsername { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string AdminPassword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? RestoreError { get; set; }

    [ObservableProperty]
    public partial bool IsRestoring { get; set; }

    /// <summary>The data was replaced: every screen still shows the old data, so the app must be reopened.</summary>
    [ObservableProperty]
    public partial bool IsRestartRequired { get; set; }

    [ObservableProperty]
    public partial string RestoreDoneText { get; set; } = string.Empty;

    /// <summary>«بازیابی آخرین نسخه».</summary>
    [RelayCommand]
    private void RestoreLatest()
    {
        if (_latestBackupFilePath is { } path)
        {
            BeginRestore(path, $"آخرین نسخه‌ی پشتیبان ({LatestBackupDateText})");
        }
    }

    /// <summary>«بازیابی از فایل…» — the page shows the file picker and hands the chosen path here.</summary>
    public void BeginRestoreFromFile(string filePath) =>
        BeginRestore(filePath, $"فایل «{Path.GetFileName(filePath)}»");

    private void BeginRestore(string filePath, string sourceText)
    {
        _restoreFilePath = filePath;
        RestoreWarningText =
            $"همه‌ی اطلاعات فعلی برنامه (کالاها، فاکتورها، مشتری‌ها، موجودی و …) با اطلاعات {sourceText} جایگزین می‌شود "
            + "و هر چیزی که بعد از آن ثبت شده از برنامه برداشته می‌شود. قبل از آن، خودکار یک نسخه‌ی ایمنی از اطلاعات فعلی گرفته می‌شود "
            + "تا اگر اشتباه شد بشود برگرداند. بعد از بازیابی، برنامه باید بسته و دوباره باز شود.";
        RestoreError = null;
        StatusMessage = null;
        ErrorMessage = null;
        IsRestoreWarningOpen = true;
    }

    [RelayCommand]
    private void ContinueRestore()
    {
        IsRestoreWarningOpen = false;
        AdminUsername = string.Empty;
        AdminPassword = string.Empty;
        RestoreError = null;
        IsRestoreCredentialsOpen = true;
    }

    [RelayCommand]
    private void CancelRestore()
    {
        IsRestoreWarningOpen = false;
        IsRestoreCredentialsOpen = false;
        AdminPassword = string.Empty;
        _restoreFilePath = null;
    }

    [RelayCommand]
    private async Task ConfirmRestoreAsync()
    {
        if (_restoreFilePath is not { } path || IsRestoring)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(AdminUsername) || string.IsNullOrEmpty(AdminPassword))
        {
            RestoreError = "نام کاربری و رمز مدیر را وارد کنید.";
            return;
        }

        IsRestoring = true;
        RestoreError = null;
        try
        {
            var result = await _restore.ExecuteAsync(
                new RestoreBackupCommand(path, _backupDirectory, AdminUsername.Trim(), AdminPassword),
                CancellationToken.None).ConfigureAwait(true);
            AdminPassword = string.Empty;
            if (!result.IsSuccess)
            {
                RestoreError = result.Error?.Message ?? "بازیابی انجام نشد.";
                return;
            }

            IsRestoreCredentialsOpen = false;
            RestoreDoneText =
                "بازیابی انجام شد. برنامه باید بسته و دوباره باز شود؛ بعد وارد شوید.\n"
                + $"نسخه‌ی ایمنی اطلاعات قبل از بازیابی: {result.Value!.SafetyBackupPath}"
                + (result.Value.Warning is { } warning ? $"\n{warning}" : string.Empty);
            IsRestartRequired = true;
        }
        finally
        {
            IsRestoring = false;
        }
    }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool DatabaseReachable { get; set; }

    [ObservableProperty]
    public partial string MigrationsText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasLatestBackup { get; set; }

    [ObservableProperty]
    public partial string LatestBackupDateText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LatestBackupStatusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool CanVerifyLatest { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        try
        {
            var health = await _health.ExecuteAsync(cancellationToken).ConfigureAwait(true);
            Apply(health);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task CreateBackupAsync()
    {
        ErrorMessage = null;
        StatusMessage = null;
        IsLoading = true;
        try
        {
            var result = await _create.ExecuteAsync(new CreateBackupCommand(_backupDirectory), CancellationToken.None).ConfigureAwait(true);
            if (!result.IsSuccess)
            {
                ErrorMessage = result.Error?.Message ?? "گرفتن نسخه پشتیبان ناموفق بود.";
                return;
            }

            StatusMessage = "نسخه پشتیبان گرفته شد — هنوز اعتبارسنجی نشده؛ روی «بررسی اعتبار» بزن.";
            var health = await _health.ExecuteAsync(CancellationToken.None).ConfigureAwait(true);
            Apply(health);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task VerifyLatestAsync()
    {
        if (_latestBackupId is not { } id)
        {
            return;
        }

        ErrorMessage = null;
        StatusMessage = null;
        IsLoading = true;
        try
        {
            var result = await _verify.ExecuteAsync(new VerifyBackupCommand(id), CancellationToken.None).ConfigureAwait(true);
            if (!result.IsSuccess)
            {
                ErrorMessage = result.Error?.Message ?? "بررسی اعتبار ناموفق بود.";
                return;
            }

            StatusMessage = result.Value!.IsVerified
                ? $"اعتبار تأیید شد: {result.Value.Note}"
                : $"بازیابی آزمایشی شکست خورد: {result.Value.Note}";
            var health = await _health.ExecuteAsync(CancellationToken.None).ConfigureAwait(true);
            Apply(health);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void Apply(SystemHealthView health)
    {
        DatabaseReachable = health.DatabaseReachable;
        MigrationsText = health.DatabaseReachable
            ? (health.MigrationsUpToDate
                ? $"{PersianNumber.FormatGrouped(health.AppliedMigrationCount)} از {PersianNumber.FormatGrouped(health.ExpectedMigrationCount)} — به‌روز"
                : $"{PersianNumber.FormatGrouped(health.AppliedMigrationCount)} از {PersianNumber.FormatGrouped(health.ExpectedMigrationCount)} — عقب‌افتاده")
            : health.DatabaseError ?? "دیتابیس در دسترس نیست.";

        _latestBackupId = health.LatestBackupId;
        _latestBackupFilePath = health.LatestBackupFilePath;
        CanRestoreLatest = health.LatestBackupFilePath is not null;
        HasLatestBackup = health.LatestBackupId is not null;
        CanVerifyLatest = health.LatestBackupId is not null;
        LatestBackupDateText = health.LatestBackupAtUtc is { } at
            ? $"{SalesText.LongDate(PersianDate.FromUtc(at))} {SalesText.ClockTime(at)}"
            : string.Empty;
        LatestBackupStatusText = health.LatestBackupStatus switch
        {
            BackupVerificationStatus.Verified => "معتبر (بازیابی و بررسی شد)",
            BackupVerificationStatus.Failed => "نامعتبر — بازیابی آزمایشی شکست خورد",
            BackupVerificationStatus.NotVerified => "هنوز بررسی نشده",
            _ => string.Empty,
        };
    }
}
