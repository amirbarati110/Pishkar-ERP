using ERP.Application.Backups;
using ERP.Application.Common;
using ERP.Domain.Backups;
using ERP.Persistence.Audit;
using ERP.Persistence.Database;
using ERP.Persistence.Identity;
using ERP.Persistence.Services;

namespace ERP.Persistence.Backups;

/// <summary>Composition root for the Backups module, same one-unit-of-work-per-call shape as the other Firebird*Service classes.</summary>
public sealed class FirebirdBackupService :
    ICreateBackupHandler,
    IVerifyBackupHandler,
    IGetSystemHealthHandler,
    IRestoreBackupHandler
{
    private readonly FirebirdConnectionFactory _connectionFactory;
    private readonly FirebirdOptions _options;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;

    public FirebirdBackupService(
        FirebirdConnectionFactory connectionFactory,
        FirebirdOptions options,
        IUserContext userContext,
        IClock clock)
    {
        _connectionFactory = connectionFactory;
        _options = options;
        _userContext = userContext;
        _clock = clock;
    }

    public async Task<Result<BackupRecordId>> ExecuteAsync(CreateBackupCommand command, CancellationToken cancellationToken)
    {
        await using var unitOfWork = await FirebirdUnitOfWork
            .CreateAsync(_connectionFactory, cancellationToken)
            .ConfigureAwait(false);
        return await new CreateBackupHandler(
                new FirebirdBackupEngine(_options),
                new FirebirdBackupRecordRepository(unitOfWork),
                new FirebirdAuditWriter(unitOfWork),
                unitOfWork,
                _userContext,
                _clock)
            .ExecuteAsync(command, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Result<VerifyBackupResult>> ExecuteAsync(VerifyBackupCommand command, CancellationToken cancellationToken)
    {
        await using var unitOfWork = await FirebirdUnitOfWork
            .CreateAsync(_connectionFactory, cancellationToken)
            .ConfigureAwait(false);
        return await new VerifyBackupHandler(
                new FirebirdBackupRecordRepository(unitOfWork),
                new FirebirdBackupEngine(_options),
                new FirebirdAuditWriter(unitOfWork),
                unitOfWork,
                _userContext,
                _clock)
            .ExecuteAsync(command, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>No unit of work around this one: the database it would open is the one being replaced.</summary>
    public Task<Result<RestoredBackup>> ExecuteAsync(RestoreBackupCommand command, CancellationToken cancellationToken) =>
        new RestoreBackupHandler(
                new FirebirdBackupEngine(_options),
                new FirebirdIdentityService(_connectionFactory),
                new FirebirdRestoreRecorder(_connectionFactory),
                _userContext,
                _clock)
            .ExecuteAsync(command, cancellationToken);

    public async Task<SystemHealthView> ExecuteAsync(CancellationToken cancellationToken)
    {
        await using var unitOfWork = await FirebirdUnitOfWork
            .CreateAsync(_connectionFactory, cancellationToken)
            .ConfigureAwait(false);
        return await new GetSystemHealthHandler(
                new FirebirdMigrationStatusReader(_connectionFactory),
                new FirebirdBackupRecordRepository(unitOfWork),
                FirebirdDatabaseBootstrapper.Migrations.Count)
            .ExecuteAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
