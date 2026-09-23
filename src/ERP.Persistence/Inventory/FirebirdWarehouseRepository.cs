using System.Data;
using System.Globalization;
using ERP.Application.Common;
using ERP.Application.Inventory;
using ERP.Domain.Cashiering;
using ERP.Domain.Inventory;
using ERP.Persistence.Database;
using FirebirdSql.Data.FirebirdClient;

namespace ERP.Persistence.Inventory;

public sealed class FirebirdWarehouseRepository : IWarehouseRepository
{
    private const string SelectColumns = "ID, NAME, ADDRESS, STATUS";

    private readonly FirebirdUnitOfWork _unitOfWork;

    public FirebirdWarehouseRepository(FirebirdUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Warehouse?> GetByIdAsync(WarehouseId warehouseId, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand($"SELECT {SelectColumns} FROM WAREHOUSE WHERE ID = @ID");
        command.Parameters.Add("@ID", FbDbType.Char).Value = warehouseId.ToString();
        return await ReadSingleAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Warehouse?> FindByNameAsync(string name, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand($"SELECT {SelectColumns} FROM WAREHOUSE WHERE NAME = @NAME");
        command.Parameters.Add("@NAME", FbDbType.VarChar).Value = name;
        return await ReadSingleAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task AddAsync(Warehouse warehouse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(warehouse);

        await using var command = CreateCommand(
            """
            INSERT INTO WAREHOUSE (ID, NAME, ADDRESS, STATUS)
            VALUES (@ID, @NAME, @ADDRESS, @STATUS)
            """);
        AddParameters(command, warehouse);

        await WriteAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAsync(Warehouse warehouse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(warehouse);

        await using var command = CreateCommand(
            """
            UPDATE WAREHOUSE
            SET NAME = @NAME, ADDRESS = @ADDRESS, STATUS = @STATUS
            WHERE ID = @ID
            """);
        AddParameters(command, warehouse);

        await WriteAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> CountActiveAsync(CancellationToken cancellationToken)
    {
        await using var command = CreateCommand("SELECT COUNT(*) FROM WAREHOUSE WHERE STATUS = @ACTIVE");
        command.Parameters.Add("@ACTIVE", FbDbType.SmallInt).Value = (short)WarehouseStatus.Active;
        var count = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(count, CultureInfo.InvariantCulture);
    }

    public async Task<bool> HasStockAsync(WarehouseId warehouseId, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(
            """
            SELECT COUNT(*)
            FROM INVENTORY_LAYER
            WHERE WAREHOUSE_ID = @WAREHOUSE_ID AND REMAINING_QTY > 0
            """);
        command.Parameters.Add("@WAREHOUSE_ID", FbDbType.Char).Value = warehouseId.ToString();
        var count = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(count, CultureInfo.InvariantCulture) > 0;
    }

    public async Task<bool> HasOpenCashShiftAsync(WarehouseId warehouseId, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(
            """
            SELECT COUNT(*)
            FROM CASH_SHIFT
            WHERE WAREHOUSE_ID = @WAREHOUSE_ID AND STATUS = @OPEN
            """);
        command.Parameters.Add("@WAREHOUSE_ID", FbDbType.Char).Value = warehouseId.ToString();
        command.Parameters.Add("@OPEN", FbDbType.SmallInt).Value = (short)CashShiftStatus.Open;
        var count = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(count, CultureInfo.InvariantCulture) > 0;
    }

    private static void AddParameters(FbCommand command, Warehouse warehouse)
    {
        command.Parameters.Add("@ID", FbDbType.Char).Value = warehouse.Id.ToString();
        command.Parameters.Add("@NAME", FbDbType.VarChar).Value = warehouse.Name;
        command.Parameters.Add("@ADDRESS", FbDbType.VarChar).Value = warehouse.Address is { } address ? address : DBNull.Value;
        command.Parameters.Add("@STATUS", FbDbType.SmallInt).Value = (short)warehouse.Status;
    }

    private static async Task WriteAsync(FbCommand command, CancellationToken cancellationToken)
    {
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (FbException exception) when (FirebirdConstraint.IsViolation(exception, "UQ_WAREHOUSE_NAME"))
        {
            throw new DataConflictException(
                "inventory.warehouse.duplicate-name",
                "انباری با همین نام همین حالا ثبت شد.",
                exception);
        }
    }

    private static async Task<Warehouse?> ReadSingleAsync(FbCommand command, CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return Warehouse.Rehydrate(
            WarehouseId.From(Guid.Parse(reader.GetString(0))),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            (WarehouseStatus)reader.GetInt16(3));
    }

    private FbCommand CreateCommand(string commandText)
    {
        return new FbCommand(commandText, _unitOfWork.Connection, _unitOfWork.Transaction)
        {
            CommandType = CommandType.Text,
        };
    }
}
