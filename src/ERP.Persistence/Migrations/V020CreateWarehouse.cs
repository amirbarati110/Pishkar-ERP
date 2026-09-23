namespace ERP.Persistence.Migrations;

/// <summary>
/// «لیست انبارها» (checklist «س»): a real, listable WAREHOUSE table in place of the single
/// hard-coded id (`FirebirdDatabaseBootstrapper.MainWarehouseId`) every sale, cash shift, opening
/// stock and stock movement already pointed at. That id becomes this migration's seed row — the
/// same GUID, so every table that already references it keeps working with no data migration —
/// under the name «فروشگاه مرکزی» that the dashboard's header already showed as plain text.
///
/// <para>The row is inserted here, inside the migration itself, rather than the idempotent
/// re-seed-on-every-boot style <c>FirebirdDatabaseBootstrapper</c> uses for CATEGORY/PRODUCT_UNIT:
/// V021 adds foreign keys from SALE/CASH_SHIFT/SALE_RETURN/INVENTORY_LAYER/STOCK_MOVEMENT to this
/// table, and those need the row to already exist for an upgrade on a database that already has
/// rows in those tables — a migration only ever runs once, so a plain INSERT is exactly right.</para>
/// </summary>
public sealed class V020CreateWarehouse : IMigration
{
    public int Version => 20;

    public string Name => "Create warehouse and seed the existing main warehouse";

    public IReadOnlyList<string> Statements { get; } =
    [
        """
        CREATE TABLE WAREHOUSE (
            ID CHAR(36) CHARACTER SET ASCII NOT NULL PRIMARY KEY,
            NAME VARCHAR(120) CHARACTER SET UTF8 NOT NULL,
            ADDRESS VARCHAR(500) CHARACTER SET UTF8,
            STATUS SMALLINT NOT NULL,
            CONSTRAINT UQ_WAREHOUSE_NAME UNIQUE (NAME)
        )
        """,
        """
        INSERT INTO WAREHOUSE (ID, NAME, ADDRESS, STATUS)
        VALUES ('33333333-3333-4333-8333-333333333333', 'فروشگاه مرکزی', NULL, 1)
        """,
    ];
}
