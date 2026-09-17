using System.Data;
using ERP.Application.Backups;
using ERP.Domain.Backups;
using ERP.Persistence.Database;
using FirebirdSql.Data.FirebirdClient;

namespace ERP.Persistence.Backups;

public sealed class FirebirdBackupRecordRepository : IBackupRecordRepository
{
    private const string SelectColumns =
        "ID, FILE_PATH, SIZE_BYTES, CREATED_AT_UTC, STATUS, VERIFIED_AT_UTC, VERIFICATION_NOTE";

    private readonly FirebirdUnitOfWork _unitOfWork;

    public FirebirdBackupRecordRepository(FirebirdUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task SaveAsync(BackupRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);

        var existing = await GetByIdAsync(record.Id, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            await using var insert = CreateCommand(
                """
                INSERT INTO BACKUP_RECORD (
                    ID, FILE_PATH, SIZE_BYTES, CREATED_AT_UTC, STATUS, VERIFIED_AT_UTC, VERIFICATION_NOTE)
                VALUES (
                    @ID, @FILE_PATH, @SIZE_BYTES, @CREATED_AT_UTC, @STATUS, @VERIFIED_AT_UTC, @VERIFICATION_NOTE)
                """);
            insert.Parameters.Add("@ID", FbDbType.Char).Value = record.Id.ToString();
            insert.Parameters.Add("@FILE_PATH", FbDbType.VarChar).Value = record.FilePath;
            insert.Parameters.Add("@SIZE_BYTES", FbDbType.BigInt).Value = record.SizeBytes;
            insert.Parameters.Add("@CREATED_AT_UTC", FbDbType.TimeStamp).Value = record.CreatedAtUtc.UtcDateTime;
            AddStatusParameters(insert, record);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await using var update = CreateCommand(
            """
            UPDATE BACKUP_RECORD SET STATUS = @STATUS, VERIFIED_AT_UTC = @VERIFIED_AT_UTC, VERIFICATION_NOTE = @VERIFICATION_NOTE
            WHERE ID = @ID
            """);
        update.Parameters.Add("@ID", FbDbType.Char).Value = record.Id.ToString();
        AddStatusParameters(update, record);
        await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<BackupRecord?> GetByIdAsync(BackupRecordId id, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand($"SELECT {SelectColumns} FROM BACKUP_RECORD WHERE ID = @ID");
        command.Parameters.Add("@ID", FbDbType.Char).Value = id.ToString();
        return await ReadSingleAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<BackupRecord?> GetLatestAsync(CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(
            $"SELECT FIRST 1 {SelectColumns} FROM BACKUP_RECORD ORDER BY CREATED_AT_UTC DESC");
        return await ReadSingleAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<BackupRecord>> ListRecentAsync(int count, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(
            $"SELECT FIRST {count} {SelectColumns} FROM BACKUP_RECORD ORDER BY CREATED_AT_UTC DESC");
        var results = new List<BackupRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(ReadRecord(reader));
        }

        return results;
    }

    private static void AddStatusParameters(FbCommand command, BackupRecord record)
    {
        command.Parameters.Add("@STATUS", FbDbType.SmallInt).Value = (short)record.Status;
        command.Parameters.Add("@VERIFIED_AT_UTC", FbDbType.TimeStamp).Value =
            record.VerifiedAtUtc is { } verifiedAt ? verifiedAt.UtcDateTime : DBNull.Value;
        command.Parameters.Add("@VERIFICATION_NOTE", FbDbType.VarChar).Value =
            record.VerificationNote is { } note ? note : DBNull.Value;
    }

    private static async Task<BackupRecord?> ReadSingleAsync(FbCommand command, CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadRecord(reader) : null;
    }

    private static BackupRecord ReadRecord(FbDataReader reader)
    {
        return BackupRecord.Rehydrate(
            BackupRecordId.From(Guid.Parse(reader.GetString(0))),
            reader.GetString(1),
            reader.GetInt64(2),
            new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(3), DateTimeKind.Utc)),
            (BackupVerificationStatus)reader.GetInt16(4),
            reader.IsDBNull(5) ? null : new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(5), DateTimeKind.Utc)),
            reader.IsDBNull(6) ? null : reader.GetString(6));
    }

    private FbCommand CreateCommand(string commandText)
    {
        return new FbCommand(commandText, _unitOfWork.Connection, _unitOfWork.Transaction)
        {
            CommandType = CommandType.Text,
        };
    }
}
