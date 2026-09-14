using System.Data;
using ERP.Application.Audit;
using ERP.Persistence.Database;
using FirebirdSql.Data.FirebirdClient;

namespace ERP.Persistence.Audit;

public sealed class FirebirdAuditWriter : IAuditWriter
{
    private readonly FirebirdUnitOfWork _unitOfWork;

    public FirebirdAuditWriter(FirebirdUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);

        await using var command = new FbCommand(
            """
            INSERT INTO AUDIT_ENTRY (
                ID, ACTOR_USER_ID, ACTION_NAME, ENTITY_TYPE, ENTITY_ID,
                OLD_VALUE, NEW_VALUE, OCCURRED_AT_UTC)
            VALUES (
                @ID, @ACTOR_USER_ID, @ACTION_NAME, @ENTITY_TYPE, @ENTITY_ID,
                @OLD_VALUE, @NEW_VALUE, @OCCURRED_AT_UTC)
            """,
            _unitOfWork.Connection,
            _unitOfWork.Transaction)
        {
            CommandType = CommandType.Text,
        };
        command.Parameters.Add("@ID", FbDbType.Char).Value = entry.Id.ToString("D");
        command.Parameters.Add("@ACTOR_USER_ID", FbDbType.Char).Value = entry.ActorUserId.ToString("D");
        command.Parameters.Add("@ACTION_NAME", FbDbType.VarChar).Value = entry.Action;
        command.Parameters.Add("@ENTITY_TYPE", FbDbType.VarChar).Value = entry.EntityType;
        command.Parameters.Add("@ENTITY_ID", FbDbType.VarChar).Value = entry.EntityId;
        command.Parameters.Add("@OLD_VALUE", FbDbType.Text).Value = entry.OldValue is { } oldValue
            ? oldValue
            : DBNull.Value;
        command.Parameters.Add("@NEW_VALUE", FbDbType.Text).Value = entry.NewValue is { } newValue
            ? newValue
            : DBNull.Value;
        command.Parameters.Add("@OCCURRED_AT_UTC", FbDbType.TimeStamp).Value = entry.OccurredAtUtc.UtcDateTime;

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
