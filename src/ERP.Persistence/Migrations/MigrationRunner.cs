using FirebirdSql.Data.FirebirdClient;
using ERP.Persistence.Database;

namespace ERP.Persistence.Migrations;

public sealed class MigrationRunner
{
    private const string CreateHistoryTable = """
        CREATE TABLE SCHEMA_MIGRATIONS (
            VERSION INTEGER NOT NULL PRIMARY KEY,
            NAME VARCHAR(150) CHARACTER SET UTF8 NOT NULL,
            APPLIED_AT_UTC TIMESTAMP NOT NULL
        )
        """;

    private readonly FirebirdConnectionFactory _connectionFactory;
    private readonly IMigration[] _migrations;

    public MigrationRunner(
        FirebirdConnectionFactory connectionFactory,
        IEnumerable<IMigration> migrations)
    {
        _connectionFactory = connectionFactory;
        _migrations = migrations.OrderBy(migration => migration.Version).ToArray();

        if (_migrations.Select(migration => migration.Version).Distinct().Count() != _migrations.Length)
        {
            throw new ArgumentException("نسخه Migration تکراری است.", nameof(migrations));
        }
    }

    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await EnsureHistoryTableAsync(connection, cancellationToken).ConfigureAwait(false);
        var appliedVersions = await ReadAppliedVersionsAsync(connection, cancellationToken).ConfigureAwait(false);

        foreach (var migration in _migrations.Where(item => !appliedVersions.Contains(item.Version)))
        {
            await ApplyAsync(connection, migration, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task EnsureHistoryTableAsync(
        FbConnection connection,
        CancellationToken cancellationToken)
    {
        const string tableExistsSql = """
            SELECT COUNT(*)
            FROM RDB$RELATIONS
            WHERE RDB$RELATION_NAME = 'SCHEMA_MIGRATIONS'
            """;
        await using var existsCommand = new FbCommand(tableExistsSql, connection);
        var exists = Convert.ToInt32(
            await existsCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            System.Globalization.CultureInfo.InvariantCulture) > 0;

        if (exists)
        {
            return;
        }

        await using var createCommand = new FbCommand(CreateHistoryTable, connection);
        await createCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<HashSet<int>> ReadAppliedVersionsAsync(
        FbConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = new FbCommand("SELECT VERSION FROM SCHEMA_MIGRATIONS", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var versions = new HashSet<int>();

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            versions.Add(reader.GetInt32(0));
        }

        return versions;
    }

    private static async Task ApplyAsync(
        FbConnection connection,
        IMigration migration,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            foreach (var statement in migration.Statements)
            {
                await using var command = new FbCommand(statement, connection, (FbTransaction)transaction);
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            const string insertSql = """
                INSERT INTO SCHEMA_MIGRATIONS (VERSION, NAME, APPLIED_AT_UTC)
                VALUES (@version, @name, @appliedAtUtc)
                """;
            await using var historyCommand = new FbCommand(insertSql, connection, (FbTransaction)transaction);
            historyCommand.Parameters.AddWithValue("version", migration.Version);
            historyCommand.Parameters.AddWithValue("name", migration.Name);
            historyCommand.Parameters.AddWithValue("appliedAtUtc", DateTime.UtcNow);
            await historyCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }
}
