using ERP.Application.Backups;
using ERP.Application.Common;
using ERP.Domain.Backups;
using ERP.Presentation.Features.Backups;

namespace ERP.Desktop.Tests.Features.Backups;

public sealed class BackupViewModelTests
{
    [Fact]
    public async Task LoadingWithNoBackupYetShowsHealthWithoutALatestBackup()
    {
        var backend = new FakeBackupBackend();
        var viewModel = new BackupViewModel(backend, backend, backend, backend, @"D:\backups");

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.True(viewModel.DatabaseReachable);
        Assert.Contains("به‌روز", viewModel.MigrationsText, StringComparison.Ordinal);
        Assert.False(viewModel.HasLatestBackup);
        Assert.False(viewModel.CanVerifyLatest);
    }

    [Fact]
    public async Task CreatingABackupShowsItAsNotVerifiedYet()
    {
        var backend = new FakeBackupBackend();
        var viewModel = new BackupViewModel(backend, backend, backend, backend, @"D:\backups");
        await viewModel.LoadAsync(CancellationToken.None);

        await viewModel.CreateBackupCommand.ExecuteAsync(null);

        Assert.Null(viewModel.ErrorMessage);
        Assert.True(viewModel.HasLatestBackup);
        Assert.True(viewModel.CanVerifyLatest);
        Assert.Contains("بررسی نشده", viewModel.LatestBackupStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerifyingAfterABackupMarksItVerified()
    {
        var backend = new FakeBackupBackend();
        var viewModel = new BackupViewModel(backend, backend, backend, backend, @"D:\backups");
        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.CreateBackupCommand.ExecuteAsync(null);

        await viewModel.VerifyLatestCommand.ExecuteAsync(null);

        Assert.Null(viewModel.ErrorMessage);
        Assert.Contains("معتبر", viewModel.LatestBackupStatusText, StringComparison.Ordinal);
        Assert.Contains("تأیید شد", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFailedBackupShowsAPersianErrorAndAddsNoRecord()
    {
        var backend = new FakeBackupBackend { FailBackup = true };
        var viewModel = new BackupViewModel(backend, backend, backend, backend, @"D:\backups");
        await viewModel.LoadAsync(CancellationToken.None);

        await viewModel.CreateBackupCommand.ExecuteAsync(null);

        Assert.NotNull(viewModel.ErrorMessage);
        Assert.False(viewModel.HasLatestBackup);
    }

    [Fact]
    public async Task RestoringTheLatestBackupWarnsThenAsksForAManagerThenRequiresAReopen()
    {
        var backend = new FakeBackupBackend();
        var viewModel = new BackupViewModel(backend, backend, backend, backend, @"D:\backups");
        await viewModel.CreateBackupCommand.ExecuteAsync(null);
        Assert.True(viewModel.CanRestoreLatest);

        viewModel.RestoreLatestCommand.Execute(null);
        Assert.True(viewModel.IsRestoreWarningOpen);
        Assert.Contains("نسخه‌ی ایمنی", viewModel.RestoreWarningText, StringComparison.Ordinal);
        Assert.Null(backend.Restored); // nothing happens on the warning

        viewModel.ContinueRestoreCommand.Execute(null);
        Assert.False(viewModel.IsRestoreWarningOpen);
        Assert.True(viewModel.IsRestoreCredentialsOpen);
        viewModel.AdminUsername = " admin ";
        viewModel.AdminPassword = "Pishkar-1405";
        await viewModel.ConfirmRestoreCommand.ExecuteAsync(null);

        Assert.Equal(@"D:\backups\backup-1.fbk", backend.Restored!.BackupFilePath);
        Assert.Equal("admin", backend.Restored.AdminUsername);
        Assert.Equal(@"D:\backups", backend.Restored.SafetyBackupDirectory);
        Assert.True(viewModel.IsRestartRequired);
        Assert.Contains("before-restore", viewModel.RestoreDoneText, StringComparison.Ordinal);
        Assert.Equal(string.Empty, viewModel.AdminPassword); // never kept around
    }

    [Fact]
    public async Task AWrongPasswordKeepsTheQuestionOpenWithTheReason()
    {
        var backend = new FakeBackupBackend { RestoreFailure = "نام کاربری یا رمز مدیر درست نیست." };
        var viewModel = new BackupViewModel(backend, backend, backend, backend, @"D:\backups");
        viewModel.BeginRestoreFromFile(@"E:\flash\backup-old.fbk");
        Assert.Contains("backup-old.fbk", viewModel.RestoreWarningText, StringComparison.Ordinal);
        viewModel.ContinueRestoreCommand.Execute(null);
        viewModel.AdminUsername = "admin";
        viewModel.AdminPassword = "wrong";

        await viewModel.ConfirmRestoreCommand.ExecuteAsync(null);

        Assert.Equal("نام کاربری یا رمز مدیر درست نیست.", viewModel.RestoreError);
        Assert.True(viewModel.IsRestoreCredentialsOpen);
        Assert.False(viewModel.IsRestartRequired);
    }

    [Fact]
    public void BlankCredentialsAreCaughtBeforeAskingAndCancelForgetsTheFile()
    {
        var backend = new FakeBackupBackend();
        var viewModel = new BackupViewModel(backend, backend, backend, backend, @"D:\backups");
        viewModel.BeginRestoreFromFile(@"E:\flash\backup-old.fbk");
        viewModel.ContinueRestoreCommand.Execute(null);

        viewModel.ConfirmRestoreCommand.Execute(null);
        Assert.Equal("نام کاربری و رمز مدیر را وارد کنید.", viewModel.RestoreError);

        viewModel.CancelRestoreCommand.Execute(null);
        viewModel.AdminUsername = "admin";
        viewModel.AdminPassword = "x";
        viewModel.ConfirmRestoreCommand.Execute(null);

        Assert.False(viewModel.IsRestoreCredentialsOpen);
        Assert.Null(backend.Restored);
    }

    private sealed class FakeBackupBackend : ICreateBackupHandler, IVerifyBackupHandler, IGetSystemHealthHandler, IRestoreBackupHandler
    {
        private BackupRecord? _latest;

        public bool FailBackup { get; set; }

        public string? RestoreFailure { get; init; }

        public RestoreBackupCommand? Restored { get; private set; }

        public Task<Result<RestoredBackup>> ExecuteAsync(RestoreBackupCommand command, CancellationToken cancellationToken)
        {
            if (RestoreFailure is { } failure)
            {
                return Task.FromResult(Result.Failure<RestoredBackup>("backup.restore.not-admin", failure));
            }

            Restored = command;
            return Task.FromResult(Result.Success(new RestoredBackup(@"D:\backups\before-restore-1.fbk", null)));
        }

        public Task<Result<BackupRecordId>> ExecuteAsync(CreateBackupCommand command, CancellationToken cancellationToken)
        {
            if (FailBackup)
            {
                return Task.FromResult(Result.Failure<BackupRecordId>("backup.create.failed", "گرفتن نسخه پشتیبان ناموفق بود: دیسک پر است."));
            }

            _latest = BackupRecord.Create(@"D:\backups\backup-1.fbk", 1000, DateTimeOffset.UtcNow);
            return Task.FromResult(Result.Success(_latest.Id));
        }

        public Task<Result<VerifyBackupResult>> ExecuteAsync(VerifyBackupCommand command, CancellationToken cancellationToken)
        {
            _latest!.MarkVerified(DateTimeOffset.UtcNow, "۲ ردیف در تاریخچه‌ی migration بازخوانی شد.");
            return Task.FromResult(Result.Success(new VerifyBackupResult(true, _latest.VerificationNote!)));
        }

        public Task<SystemHealthView> ExecuteAsync(CancellationToken cancellationToken) => Task.FromResult(new SystemHealthView(
            true, null, 12, 12, true, _latest?.Id, _latest?.CreatedAtUtc, _latest?.Status, _latest?.FilePath));
    }
}
