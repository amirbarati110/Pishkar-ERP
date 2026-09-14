using System.Data;
using ERP.Application.Inventory;
using ERP.Domain.Catalog;
using ERP.Domain.Common;
using ERP.Domain.Inventory;
using ERP.Persistence.Database;
using FirebirdSql.Data.FirebirdClient;

namespace ERP.Persistence.Inventory;

public sealed class FirebirdStockLedgerRepository : IStockLedgerRepository
{
    private readonly FirebirdUnitOfWork _unitOfWork;

    public FirebirdStockLedgerRepository(FirebirdUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<StockLedger?> GetAsync(
        ProductId productId,
        WarehouseId warehouseId,
        CancellationToken cancellationToken)
    {
        var layers = await ReadLayersAsync(productId, warehouseId, cancellationToken).ConfigureAwait(false);
        var movements = await ReadMovementsAsync(productId, warehouseId, cancellationToken).ConfigureAwait(false);

        if (layers.Count == 0 && movements.Count == 0)
        {
            return null;
        }

        return StockLedger.Rehydrate(productId, warehouseId, layers, movements);
    }

    public async Task SaveAsync(StockLedger ledger, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ledger);

        foreach (var layer in ledger.Layers)
        {
            await SaveLayerAsync(ledger, layer, cancellationToken).ConfigureAwait(false);
        }

        foreach (var movement in ledger.Movements)
        {
            await SaveMovementAsync(movement, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<IReadOnlyList<InventoryLayer>> ReadLayersAsync(
        ProductId productId,
        WarehouseId warehouseId,
        CancellationToken cancellationToken)
    {
        var layers = new List<InventoryLayer>();
        await using var command = CreateCommand(
            """
            SELECT ID, ORIGINAL_QTY, REMAINING_QTY, UNIT_COST_RIALS, RECEIVED_ON
            FROM INVENTORY_LAYER
            WHERE PRODUCT_ID = @PRODUCT_ID AND WAREHOUSE_ID = @WAREHOUSE_ID
            ORDER BY RECEIVED_ON, ID
            """);
        AddLedgerKey(command, productId, warehouseId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            layers.Add(InventoryLayer.Rehydrate(
                new InventoryLayerId(Guid.Parse(reader.GetString(0))),
                Quantity.Create(reader.GetDecimal(1)),
                StockQuantity.From(reader.GetDecimal(2)),
                Money.FromRials(reader.GetInt64(3)),
                DateOnly.FromDateTime(reader.GetDateTime(4))));
        }

        return layers;
    }

    private async Task<IReadOnlyList<StockMovement>> ReadMovementsAsync(
        ProductId productId,
        WarehouseId warehouseId,
        CancellationToken cancellationToken)
    {
        var movements = new List<StockMovement>();
        await using var command = CreateCommand(
            """
            SELECT ID, MOVEMENT_TYPE, QUANTITY, SOURCE_REFERENCE, OCCURRED_AT_UTC
            FROM STOCK_MOVEMENT
            WHERE PRODUCT_ID = @PRODUCT_ID AND WAREHOUSE_ID = @WAREHOUSE_ID
            ORDER BY OCCURRED_AT_UTC, ID
            """);
        AddLedgerKey(command, productId, warehouseId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var occurredAt = DateTime.SpecifyKind(reader.GetDateTime(4), DateTimeKind.Utc);
            movements.Add(new StockMovement(
                new StockMovementId(Guid.Parse(reader.GetString(0))),
                productId,
                warehouseId,
                (StockMovementType)reader.GetInt16(1),
                Quantity.Create(reader.GetDecimal(2)),
                reader.GetString(3),
                new DateTimeOffset(occurredAt)));
        }

        return movements;
    }

    private async Task SaveLayerAsync(
        StockLedger ledger,
        InventoryLayer layer,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(
            """
            UPDATE OR INSERT INTO INVENTORY_LAYER (
                ID, PRODUCT_ID, WAREHOUSE_ID, ORIGINAL_QTY, REMAINING_QTY,
                UNIT_COST_RIALS, RECEIVED_ON)
            VALUES (
                @ID, @PRODUCT_ID, @WAREHOUSE_ID, @ORIGINAL_QTY, @REMAINING_QTY,
                @UNIT_COST_RIALS, @RECEIVED_ON)
            MATCHING (ID)
            """);
        command.Parameters.Add("@ID", FbDbType.Char).Value = layer.Id.ToString();
        command.Parameters.Add("@PRODUCT_ID", FbDbType.Char).Value = ledger.ProductId.ToString();
        command.Parameters.Add("@WAREHOUSE_ID", FbDbType.Char).Value = ledger.WarehouseId.ToString();
        command.Parameters.Add("@ORIGINAL_QTY", FbDbType.Decimal).Value = layer.OriginalQuantity.Value;
        command.Parameters.Add("@REMAINING_QTY", FbDbType.Decimal).Value = layer.RemainingQuantity.Value;
        command.Parameters.Add("@UNIT_COST_RIALS", FbDbType.BigInt).Value = layer.UnitCost.Rials;
        command.Parameters.Add("@RECEIVED_ON", FbDbType.Date).Value = layer.ReceivedOn.ToDateTime(TimeOnly.MinValue);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task SaveMovementAsync(
        StockMovement movement,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(
            """
            UPDATE OR INSERT INTO STOCK_MOVEMENT (
                ID, PRODUCT_ID, WAREHOUSE_ID, MOVEMENT_TYPE, QUANTITY,
                SOURCE_REFERENCE, OCCURRED_AT_UTC)
            VALUES (
                @ID, @PRODUCT_ID, @WAREHOUSE_ID, @MOVEMENT_TYPE, @QUANTITY,
                @SOURCE_REFERENCE, @OCCURRED_AT_UTC)
            MATCHING (ID)
            """);
        command.Parameters.Add("@ID", FbDbType.Char).Value = movement.Id.ToString();
        command.Parameters.Add("@PRODUCT_ID", FbDbType.Char).Value = movement.ProductId.ToString();
        command.Parameters.Add("@WAREHOUSE_ID", FbDbType.Char).Value = movement.WarehouseId.ToString();
        command.Parameters.Add("@MOVEMENT_TYPE", FbDbType.SmallInt).Value = (short)movement.Type;
        command.Parameters.Add("@QUANTITY", FbDbType.Decimal).Value = movement.Quantity.Value;
        command.Parameters.Add("@SOURCE_REFERENCE", FbDbType.VarChar).Value = movement.Reference;
        command.Parameters.Add("@OCCURRED_AT_UTC", FbDbType.TimeStamp).Value = movement.OccurredAtUtc.UtcDateTime;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void AddLedgerKey(
        FbCommand command,
        ProductId productId,
        WarehouseId warehouseId)
    {
        command.Parameters.Add("@PRODUCT_ID", FbDbType.Char).Value = productId.ToString();
        command.Parameters.Add("@WAREHOUSE_ID", FbDbType.Char).Value = warehouseId.ToString();
    }

    private FbCommand CreateCommand(string commandText)
    {
        return new FbCommand(commandText, _unitOfWork.Connection, _unitOfWork.Transaction)
        {
            CommandType = CommandType.Text,
        };
    }
}
