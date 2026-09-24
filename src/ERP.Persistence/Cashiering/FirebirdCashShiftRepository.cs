using System.Data;
using ERP.Application.Cashiering;
using ERP.Domain.Cashiering;
using ERP.Domain.Common;
using ERP.Domain.Identity;
using ERP.Domain.Inventory;
using ERP.Persistence.Database;
using FirebirdSql.Data.FirebirdClient;

namespace ERP.Persistence.Cashiering;

public sealed class FirebirdCashShiftRepository : ICashShiftRepository
{
    private const string SelectColumns =
        "ID, WAREHOUSE_ID, OPENED_BY_USER_ID, OPENED_AT_UTC, OPENING_CASH_RIALS, STATUS, "
        + "CLOSED_BY_USER_ID, CLOSED_AT_UTC, COUNTED_CASH_RIALS, CASH_SALES_RIALS, NOTE, CASH_REFUNDS_RIALS, CASH_RECEIPTS_RIALS";

    private readonly FirebirdUnitOfWork _unitOfWork;

    public FirebirdCashShiftRepository(FirebirdUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CashShift?> GetOpenAsync(WarehouseId warehouseId, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(
            $"SELECT {SelectColumns} FROM CASH_SHIFT WHERE WAREHOUSE_ID = @WAREHOUSE_ID AND STATUS = @OPEN");
        command.Parameters.Add("@WAREHOUSE_ID", FbDbType.Char).Value = warehouseId.ToString();
        command.Parameters.Add("@OPEN", FbDbType.SmallInt).Value = (short)CashShiftStatus.Open;
        return await ReadSingleAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<CashShift?> GetByIdAsync(CashShiftId id, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand($"SELECT {SelectColumns} FROM CASH_SHIFT WHERE ID = @ID");
        command.Parameters.Add("@ID", FbDbType.Char).Value = id.ToString();
        return await ReadSingleAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveAsync(CashShift shift, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(shift);

        var existing = await GetByIdAsync(shift.Id, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            await using var insert = CreateCommand(
                """
                INSERT INTO CASH_SHIFT (
                    ID, WAREHOUSE_ID, OPENED_BY_USER_ID, OPENED_AT_UTC, OPENING_CASH_RIALS, STATUS,
                    CLOSED_BY_USER_ID, CLOSED_AT_UTC, COUNTED_CASH_RIALS, CASH_SALES_RIALS, NOTE, CASH_REFUNDS_RIALS,
                    CASH_RECEIPTS_RIALS)
                VALUES (
                    @ID, @WAREHOUSE_ID, @OPENED_BY_USER_ID, @OPENED_AT_UTC, @OPENING_CASH_RIALS, @STATUS,
                    @CLOSED_BY_USER_ID, @CLOSED_AT_UTC, @COUNTED_CASH_RIALS, @CASH_SALES_RIALS, @NOTE, @CASH_REFUNDS_RIALS,
                    @CASH_RECEIPTS_RIALS)
                """);
            insert.Parameters.Add("@ID", FbDbType.Char).Value = shift.Id.ToString();
            insert.Parameters.Add("@WAREHOUSE_ID", FbDbType.Char).Value = shift.WarehouseId.ToString();
            insert.Parameters.Add("@OPENED_BY_USER_ID", FbDbType.Char).Value = shift.OpenedByUserId.ToString();
            insert.Parameters.Add("@OPENED_AT_UTC", FbDbType.TimeStamp).Value = shift.OpenedAtUtc.UtcDateTime;
            AddCommonParameters(insert, shift);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await using var update = CreateCommand(
            """
            UPDATE CASH_SHIFT SET
                STATUS = @STATUS,
                CLOSED_BY_USER_ID = @CLOSED_BY_USER_ID,
                CLOSED_AT_UTC = @CLOSED_AT_UTC,
                COUNTED_CASH_RIALS = @COUNTED_CASH_RIALS,
                CASH_SALES_RIALS = @CASH_SALES_RIALS,
                CASH_REFUNDS_RIALS = @CASH_REFUNDS_RIALS,
                CASH_RECEIPTS_RIALS = @CASH_RECEIPTS_RIALS,
                NOTE = @NOTE
            WHERE ID = @ID
            """);
        update.Parameters.Add("@ID", FbDbType.Char).Value = shift.Id.ToString();
        AddCommonParameters(update, shift);
        await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void AddCommonParameters(FbCommand command, CashShift shift)
    {
        command.Parameters.Add("@OPENING_CASH_RIALS", FbDbType.BigInt).Value = shift.OpeningCash.Rials;
        command.Parameters.Add("@STATUS", FbDbType.SmallInt).Value = (short)shift.Status;
        command.Parameters.Add("@CLOSED_BY_USER_ID", FbDbType.Char).Value =
            shift.ClosedByUserId is { } closedBy ? closedBy.ToString() : DBNull.Value;
        command.Parameters.Add("@CLOSED_AT_UTC", FbDbType.TimeStamp).Value =
            shift.ClosedAtUtc is { } closedAt ? closedAt.UtcDateTime : DBNull.Value;
        command.Parameters.Add("@COUNTED_CASH_RIALS", FbDbType.BigInt).Value =
            shift.CountedCash is { } counted ? counted.Rials : DBNull.Value;
        command.Parameters.Add("@CASH_SALES_RIALS", FbDbType.BigInt).Value =
            shift.CashSalesDuringShift is { } cashSales ? cashSales.Rials : DBNull.Value;
        command.Parameters.Add("@CASH_REFUNDS_RIALS", FbDbType.BigInt).Value =
            shift.CashRefundsDuringShift is { } cashRefunds ? cashRefunds.Rials : DBNull.Value;
        command.Parameters.Add("@CASH_RECEIPTS_RIALS", FbDbType.BigInt).Value =
            shift.CashReceiptsDuringShift is { } cashReceipts ? cashReceipts.Rials : DBNull.Value;
        command.Parameters.Add("@NOTE", FbDbType.VarChar).Value =
            shift.Note is { } note ? note : DBNull.Value;
    }

    private static async Task<CashShift?> ReadSingleAsync(FbCommand command, CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return CashShift.Rehydrate(
            CashShiftId.From(Guid.Parse(reader.GetString(0))),
            WarehouseId.From(Guid.Parse(reader.GetString(1))),
            UserId.From(Guid.Parse(reader.GetString(2))),
            new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(3), DateTimeKind.Utc)),
            Money.FromRials(reader.GetInt64(4)),
            (CashShiftStatus)reader.GetInt16(5),
            reader.IsDBNull(6) ? null : UserId.From(Guid.Parse(reader.GetString(6))),
            reader.IsDBNull(7) ? null : new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(7), DateTimeKind.Utc)),
            reader.IsDBNull(8) ? null : Money.FromRials(reader.GetInt64(8)),
            reader.IsDBNull(9) ? null : Money.FromRials(reader.GetInt64(9)),
            reader.IsDBNull(11) ? null : Money.FromRials(reader.GetInt64(11)),
            reader.IsDBNull(12) ? null : Money.FromRials(reader.GetInt64(12)),
            reader.IsDBNull(10) ? null : reader.GetString(10));
    }

    private FbCommand CreateCommand(string commandText)
    {
        return new FbCommand(commandText, _unitOfWork.Connection, _unitOfWork.Transaction)
        {
            CommandType = CommandType.Text,
        };
    }
}
