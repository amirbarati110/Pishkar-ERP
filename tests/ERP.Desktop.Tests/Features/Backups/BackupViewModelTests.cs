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
        var viewModel = new BackupViewModel(backend, backend, backend, @"D:\backups");

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
        var viewModel = new BackupViewModel(backend, backend, backend, @"D:\backups");
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
        var viewModel = new BackupViewModel(backend, backend, backend, @"D:\backups");
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
        var viewModel = new BackupViewModel(backend, backend, backend, @"D:\backups");
        await viewModel.LoadAsync(CancellationToken.None);

        await viewModel.CreateBackupCommand.ExecuteAsync(null);

        Assert.NotNull(viewModel.ErrorMessage);
        Assert.False(viewModel.HasLatestBackup);
    }

    private sealed class FakeBackupBackend : ICreateBackupHandler, IVerifyBackupHandler, IGetSystemHealthHandler
    {
        private BackupRecord? _latest;

        public bool FailBackup { get; set; }

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
            true, null, 12, 12, true, _latest?.Id, _latest?.CreatedAtUtc, _latest?.Status));
    }
}
