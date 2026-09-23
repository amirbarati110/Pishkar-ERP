namespace ERP.Persistence.Migrations;

/// <summary>
/// «لیست انبارها» (checklist «س»): a real, listable WAREHOUSE table in place of the single
/// hard-coded id (`FirebirdDatabaseBootstrapper.MainWarehouseId`) every sale, cash shift, opening
/// stock and stock movement already pointed at. That id becomes this migration's seed row — the
/// same GUID, so every table that already references it keeps working with no data migration —
/// under the name «فروشگاه مرکزی» that the dashboard's header already showed as plain text.
///
/// <para>Only the table is created here; the seed row is V021 and the foreign keys V022. Firebird
/// applies DDL when its transaction commits, so an INSERT into a table created earlier in the same
/// migration fails with «Table unknown» — the same reason V016 (columns) and V017 (backfill) are
/// separate migrations.</para>
/// </summary>
public sealed class V020CreateWarehouse : IMigration
{
    public int Version => 20;

    public string Name => "Create warehouse";

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
    ];
}
