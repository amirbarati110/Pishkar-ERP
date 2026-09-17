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
    private readonly string _backupDirectory;

    private BackupRecordId? _latestBackupId;

    public BackupViewModel(
        ICreateBackupHandler create,
        IVerifyBackupHandler verify,
        IGetSystemHealthHandler health,
        string backupDirectory)
    {
        _create = create;
        _verify = verify;
        _health = health;
        _backupDirectory = backupDirectory;
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
