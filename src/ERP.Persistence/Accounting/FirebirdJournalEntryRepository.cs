using System.Data;
using ERP.Application.Accounting;
using ERP.Domain.Accounting;
using ERP.Domain.Common;
using ERP.Persistence.Database;
using FirebirdSql.Data.FirebirdClient;

namespace ERP.Persistence.Accounting;

/// <summary>Insert-only — a <see cref="JournalEntry"/> is never edited once posted (§10.5), so unlike most repositories here there is no update path.</summary>
public sealed class FirebirdJournalEntryRepository : IJournalEntryRepository
{
    private readonly FirebirdUnitOfWork _unitOfWork;

    public FirebirdJournalEntryRepository(FirebirdUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task SaveAsync(JournalEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);

        await using var insertEntry = CreateCommand(
            "INSERT INTO JOURNAL_ENTRY (ID, SOURCE_TYPE, SOURCE_ID, POSTED_AT_UTC) VALUES (@ID, @SOURCE_TYPE, @SOURCE_ID, @POSTED_AT_UTC)");
        insertEntry.Parameters.Add("@ID", FbDbType.Char).Value = entry.Id.ToString();
        insertEntry.Parameters.Add("@SOURCE_TYPE", FbDbType.SmallInt).Value = (short)entry.SourceType;
        insertEntry.Parameters.Add("@SOURCE_ID", FbDbType.VarChar).Value = entry.SourceId;
        insertEntry.Parameters.Add("@POSTED_AT_UTC", FbDbType.TimeStamp).Value = entry.PostedAtUtc.UtcDateTime;
        await insertEntry.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        for (var index = 0; index < entry.Lines.Count; index++)
        {
            var line = entry.Lines[index];
            await using var insertLine = CreateCommand(
                """
                INSERT INTO JOURNAL_LINE (ENTRY_ID, LINE_NO, ACCOUNT, DEBIT_RIALS, CREDIT_RIALS)
                VALUES (@ENTRY_ID, @LINE_NO, @ACCOUNT, @DEBIT_RIALS, @CREDIT_RIALS)
                """);
            insertLine.Parameters.Add("@ENTRY_ID", FbDbType.Char).Value = entry.Id.ToString();
            insertLine.Parameters.Add("@LINE_NO", FbDbType.SmallInt).Value = (short)index;
            insertLine.Parameters.Add("@ACCOUNT", FbDbType.SmallInt).Value = (short)line.Account;
            insertLine.Parameters.Add("@DEBIT_RIALS", FbDbType.BigInt).Value = line.Debit.Rials;
            insertLine.Parameters.Add("@CREDIT_RIALS", FbDbType.BigInt).Value = line.Credit.Rials;
            await insertLine.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<JournalEntry?> GetBySourceAsync(JournalSourceType sourceType, string sourceId, CancellationToken cancellationToken)
    {
        await using var entryCommand = CreateCommand(
            "SELECT ID, SOURCE_TYPE, SOURCE_ID, POSTED_AT_UTC FROM JOURNAL_ENTRY WHERE SOURCE_TYPE = @SOURCE_TYPE AND SOURCE_ID = @SOURCE_ID");
        entryCommand.Parameters.Add("@SOURCE_TYPE", FbDbType.SmallInt).Value = (short)sourceType;
        entryCommand.Parameters.Add("@SOURCE_ID", FbDbType.VarChar).Value = sourceId;

        JournalEntryId id;
        DateTimeOffset postedAtUtc;
        await using (var reader = await entryCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            id = JournalEntryId.From(Guid.Parse(reader.GetString(0)));
            postedAtUtc = new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(3), DateTimeKind.Utc));
        }

        await using var lineCommand = CreateCommand(
            "SELECT ACCOUNT, DEBIT_RIALS, CREDIT_RIALS FROM JOURNAL_LINE WHERE ENTRY_ID = @ENTRY_ID ORDER BY LINE_NO");
        lineCommand.Parameters.Add("@ENTRY_ID", FbDbType.Char).Value = id.ToString();

        var lines = new List<JournalLine>();
        await using (var reader = await lineCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var account = (AccountCode)reader.GetInt16(0);
                var debit = reader.GetInt64(1);
                var credit = reader.GetInt64(2);
                lines.Add(debit > 0
                    ? JournalLine.Debited(account, Money.FromRials(debit))
                    : JournalLine.Credited(account, Money.FromRials(credit)));
            }
        }

        return JournalEntry.Rehydrate(id, sourceType, sourceId, postedAtUtc, lines);
    }

    private FbCommand CreateCommand(string commandText)
    {
        return new FbCommand(commandText, _unitOfWork.Connection, _unitOfWork.Transaction)
        {
            CommandType = CommandType.Text,
        };
    }
}
