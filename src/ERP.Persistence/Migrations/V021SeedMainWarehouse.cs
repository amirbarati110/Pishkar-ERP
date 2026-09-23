namespace ERP.Persistence.Migrations;

/// <summary>
/// The single hard-coded id (`FirebirdDatabaseBootstrapper.MainWarehouseId`) every sale, cash shift,
/// opening stock and stock movement already pointed at becomes V020's first row — the same GUID, so
/// every table that already references it keeps working with no data migration — under the name
/// «فروشگاه مرکزی» that the dashboard's header already showed as plain text.
///
/// <para>A plain INSERT here, not the idempotent re-seed-on-every-boot style
/// <c>FirebirdDatabaseBootstrapper</c> uses for CATEGORY/PRODUCT_UNIT: V022 adds foreign keys from
/// SALE/CASH_SHIFT/SALE_RETURN/INVENTORY_LAYER/STOCK_MOVEMENT to WAREHOUSE, and on an upgraded
/// database that already has rows in those tables the row must exist before V022 runs. A migration
/// only ever runs once, so a plain INSERT is exactly right.</para>
/// </summary>
public sealed class V021SeedMainWarehouse : IMigration
{
    public int Version => 21;

    public string Name => "Seed the existing main warehouse";

    public IReadOnlyList<string> Statements { get; } =
    [
        """
        INSERT INTO WAREHOUSE (ID, NAME, ADDRESS, STATUS)
        VALUES ('33333333-3333-4333-8333-333333333333', 'فروشگاه مرکزی', NULL, 1)
        """,
    ];
}
