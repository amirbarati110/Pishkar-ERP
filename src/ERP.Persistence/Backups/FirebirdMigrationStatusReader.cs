using ERP.Application.Backups;
using ERP.Persistence.Database;
using FirebirdSql.Data.FirebirdClient;

namespace ERP.Persistence.Backups;

/// <summary>Reads straight from <c>SCHEMA_MIGRATIONS</c> — the database's own record of what actually ran, not the running code's list (they can differ if this build is older or newer than the database it opened).</summary>
public sealed class FirebirdMigrationStatusReader : IMigrationStatusReader
{
    private readonly FirebirdConnectionFactory _connectionFactory;

    public FirebirdMigrationStatusReader(FirebirdConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<MigrationStatus> ReadAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new FbCommand("SELECT COUNT(*), MAX(VERSION) FROM SCHEMA_MIGRATIONS", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        await reader.ReadAsync(cancellationToken).ConfigureAwait(false);

        var appliedCount = reader.GetInt32(0);
        var latestVersion = reader.IsDBNull(1) ? (int?)null : reader.GetInt32(1);
        return new MigrationStatus(appliedCount, latestVersion);
    }
}
