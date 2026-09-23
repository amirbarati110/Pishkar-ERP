namespace ERP.Persistence.Migrations;

/// <summary>
/// Every table that stores a WAREHOUSE_ID now points at a real row (V020) — so it can be enforced,
/// closing the gap where a typo'd or stale id would silently never show up on any report.
/// </summary>
public sealed class V021WarehouseForeignKeys : IMigration
{
    public int Version => 21;

    public string Name => "Enforce warehouse references from sales, cash shifts, returns and stock";

    public IReadOnlyList<string> Statements { get; } =
    [
        "ALTER TABLE INVENTORY_LAYER ADD CONSTRAINT FK_LAYER_WAREHOUSE FOREIGN KEY (WAREHOUSE_ID) REFERENCES WAREHOUSE (ID)",
        "ALTER TABLE STOCK_MOVEMENT ADD CONSTRAINT FK_MOVEMENT_WAREHOUSE FOREIGN KEY (WAREHOUSE_ID) REFERENCES WAREHOUSE (ID)",
        "ALTER TABLE SALE ADD CONSTRAINT FK_SALE_WAREHOUSE FOREIGN KEY (WAREHOUSE_ID) REFERENCES WAREHOUSE (ID)",
        "ALTER TABLE CASH_SHIFT ADD CONSTRAINT FK_CASH_SHIFT_WAREHOUSE FOREIGN KEY (WAREHOUSE_ID) REFERENCES WAREHOUSE (ID)",
        "ALTER TABLE SALE_RETURN ADD CONSTRAINT FK_SALE_RETURN_WAREHOUSE FOREIGN KEY (WAREHOUSE_ID) REFERENCES WAREHOUSE (ID)",
    ];
}
