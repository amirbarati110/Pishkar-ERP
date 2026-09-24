using ERP.Application.Backups;
using ERP.Application.Common;
using ERP.Application.Identity;
using ERP.Application.Tests.TestDoubles;
using ERP.Domain.Identity;

namespace ERP.Application.Tests.Backups;

/// <summary>The failure paths of «بازیابی» that a real database cannot be made to hit on purpose.</summary>
public sealed class RestoreBackupHandlerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"pishkar-restore-unit-{Guid.NewGuid():N}");
    private readonly string _backupFile;

    public RestoreBackupHandlerTests()
    {
        Directory.CreateDirectory(_directory);
        _backupFile = Path.Combine(_directory, "backup-1.fbk");
        File.WriteAllText(_backupFile, "fbk");
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static RestoreBackupHandler Handler(FakeEngine engine, FakeRecorder? recorder = null)
    {
        var context = new ApplicationTestContext();
        return new RestoreBackupHandler(engine, new AlwaysAdmin(), recorder ?? new FakeRecorder(), context.User, context.Clock);
    }

    private RestoreBackupCommand Command() => new(_backupFile, _directory, "admin", "Pishkar-1405");

    [Fact]
    public async Task WhenReplacingFailsTheSafetyCopyIsPutBack()
    {
        var engine = new FakeEngine { FailReplaceOf = _backupFile };

        var result = await Handler(engine).ExecuteAsync(Command(), CancellationToken.None);

        Assert.Equal("backup.restore.failed-rolled-back", result.Error?.Code);
        Assert.Equal(2, engine.Replaced.Count);
        Assert.StartsWith(Path.Combine(_directory, "before-restore-"), engine.Replaced[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task WhenEvenTheRollbackFailsTheMessageSaysWhereTheSafetyCopyIs()
    {
        var engine = new FakeEngine { FailEveryReplace = true };

        var result = await Handler(engine).ExecuteAsync(Command(), CancellationToken.None);

        Assert.Equal("backup.restore.failed", result.Error?.Code);
        Assert.Contains("before-restore-", result.Error?.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithoutASafetyCopyNothingIsReplaced()
    {
        var engine = new FakeEngine { FailSafetyBackup = true };

        var result = await Handler(engine).ExecuteAsync(Command(), CancellationToken.None);

        Assert.Equal("backup.restore.safety-failed", result.Error?.Code);
        Assert.Empty(engine.Replaced);
    }

    [Fact]
    public async Task IfOnlyTheHistoryCannotBeWrittenTheRestoreStandsWithAWarning()
    {
        var engine = new FakeEngine();

        var result = await Handler(engine, new FakeRecorder { Fail = true }).ExecuteAsync(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value!.Warning);
        Assert.Equal([_backupFile], engine.Replaced);
    }

    [Fact]
    public async Task TwoSafetyCopiesInTheSameSecondGetDifferentNames()
    {
        var engine = new FakeEngine();
        var handler = Handler(engine);

        var first = await handler.ExecuteAsync(Command(), CancellationToken.None);
        var second = await handler.ExecuteAsync(Command(), CancellationToken.None);

        Assert.NotEqual(first.Value!.SafetyBackupPath, second.Value!.SafetyBackupPath);
    }

    private sealed class FakeEngine : IBackupEngine
    {
        public string? FailReplaceOf { get; init; }

        public bool FailEveryReplace { get; init; }

        public bool FailSafetyBackup { get; init; }

        public List<string> Replaced { get; } = [];

        public Task<long> CreateBackupFileAsync(string destinationFilePath, CancellationToken cancellationToken)
        {
            if (FailSafetyBackup)
            {
                throw new IOException("دیسک پر است.");
            }

            File.WriteAllText(destinationFilePath, "safety");
            return Task.FromResult(6L);
        }

        public Task<StructuralCheckResult> RestoreAndCheckAsync(string backupFilePath, CancellationToken cancellationToken) =>
            Task.FromResult(new StructuralCheckResult(true, "ok"));

        public Task ReplaceLiveDatabaseAsync(string backupFilePath, CancellationToken cancellationToken)
        {
            Replaced.Add(backupFilePath);
            if (FailEveryReplace || backupFilePath == FailReplaceOf)
            {
                throw new IOException("قطع برق");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FakeRecorder : IRestoreRecorder
    {
        public bool Fail { get; init; }

        public Task RecordAsync(RestoreRecord record, CancellationToken cancellationToken) =>
            Fail ? throw new IOException("ثبت نشد") : Task.CompletedTask;
    }

    private sealed class AlwaysAdmin : IVerifyAdminCredentialHandler
    {
        public Task<Result<UserId>> ExecuteAsync(VerifyAdminCredentialCommand command, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success(UserId.New()));
    }
}
