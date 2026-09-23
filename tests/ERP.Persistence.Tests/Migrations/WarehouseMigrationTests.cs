using ERP.Persistence.Database;
using ERP.Persistence.Migrations;
using ERP.Persistence.Services;
using ERP.Persistence.Tests.TestSupport;
using FirebirdSql.Data.FirebirdClient;

namespace ERP.Persistence.Tests.Migrations;

/// <summary>
/// The upgrade path of «لیست انبارها»: a database from before V020 already has rows pointing at the
/// hard-coded main warehouse id. V020–V022 must turn that id into a real WAREHOUSE row and enforce
/// the reference from then on — without touching the existing rows.
/// </summary>
public sealed class WarehouseMigrationTests
{
    private const string MainWarehouseId = "33333333-3333-4333-8333-333333333333";

    [Fact]
    public async Task ExistingRowsKeepTheirMainWarehouseWhichBecomesARealRowAndUnknownIdsAreRejected()
    {
        await using var database = FirebirdTestDatabase.Create();
        var factory = new FirebirdConnectionFactory(database.Options);
        var all = FirebirdDatabaseBootstrapper.Migrations;

        // the database as it was before warehouses were a table
        await new MigrationRunner(factory, all.Where(migration => migration.Version <= 19).ToArray())
            .MigrateAsync(CancellationToken.None);
        await using (var connection = await factory.OpenAsync(CancellationToken.None))
        {
            await InsertOpenCashShiftAsync(connection, "44444444-4444-4444-8444-444444444444", MainWarehouseId);
        }

        await new MigrationRunner(factory, all).MigrateAsync(CancellationToken.None);

        await using var read = await factory.OpenAsync(CancellationToken.None);
        await using (var select = new FbCommand("SELECT ID, NAME, STATUS FROM WAREHOUSE", read))
        await using (var reader = await select.ExecuteReaderAsync(CancellationToken.None))
        {
            Assert.True(await reader.ReadAsync(CancellationToken.None));
            Assert.Equal(MainWarehouseId, reader.GetString(0));
            Assert.Equal("فروشگاه مرکزی", reader.GetString(1));
            Assert.Equal(1, reader.GetInt16(2)); // WarehouseStatus.Active
            Assert.False(await reader.ReadAsync(CancellationToken.None));
        }

        await using (var count = new FbCommand("SELECT COUNT(*) FROM CASH_SHIFT", read))
        {
            Assert.Equal(1, Convert.ToInt32(
                await count.ExecuteScalarAsync(CancellationToken.None),
                System.Globalization.CultureInfo.InvariantCulture));
        }

        await Assert.ThrowsAsync<FbException>(() => InsertOpenCashShiftAsync(
            read, "55555555-5555-4555-8555-555555555555", "99999999-9999-4999-8999-999999999999"));
    }

    [Fact]
    public async Task AFreshDatabaseMigratesToTheLatestVersionWithTheMainWarehouse()
    {
        await using var database = FirebirdTestDatabase.Create();
        var factory = new FirebirdConnectionFactory(database.Options);

        await new MigrationRunner(factory, FirebirdDatabaseBootstrapper.Migrations).MigrateAsync(CancellationToken.None);

        await using var connection = await factory.OpenAsync(CancellationToken.None);
        await using var command = new FbCommand("SELECT COUNT(*) FROM WAREHOUSE WHERE ID = @ID", connection);
        command.Parameters.Add("@ID", FbDbType.Char).Value = MainWarehouseId;
        Assert.Equal(1, Convert.ToInt32(
            await command.ExecuteScalarAsync(CancellationToken.None),
            System.Globalization.CultureInfo.InvariantCulture));
    }

    private static async Task InsertOpenCashShiftAsync(FbConnection connection, string id, string warehouseId)
    {
        await using var insert = new FbCommand(
            """
            INSERT INTO CASH_SHIFT (ID, WAREHOUSE_ID, OPENED_BY_USER_ID, OPENED_AT_UTC, OPENING_CASH_RIALS, STATUS)
            VALUES (@ID, @WAREHOUSE_ID, '11111111-1111-4111-8111-111111111111', CURRENT_TIMESTAMP, 0, 1)
            """,
            connection);
        insert.Parameters.Add("@ID", FbDbType.Char).Value = id;
        insert.Parameters.Add("@WAREHOUSE_ID", FbDbType.Char).Value = warehouseId;
        await insert.ExecuteNonQueryAsync(CancellationToken.None);
    }
}
