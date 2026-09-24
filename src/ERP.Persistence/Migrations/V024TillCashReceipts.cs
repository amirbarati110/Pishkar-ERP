namespace ERP.Persistence.Migrations;

/// <summary>
/// Audit 1405/07/02: cash taken for «دریافت از مشتری» went into the drawer but no shift expected
/// it, so every close showed a false overage. A payment now records the till (warehouse) it was
/// taken at, and a closed shift keeps the cash it received that way beside its sales and refunds.
/// Existing payments get their till in V025 (a column added here cannot be written in this same
/// transaction — the V016/V017 rule).
/// </summary>
public sealed class V024TillCashReceipts : IMigration
{
    public int Version => 24;

    public string Name => "Record the till of a customer payment and the cash a shift received";

    public IReadOnlyList<string> Statements { get; } =
    [
        "ALTER TABLE CUSTOMER_PAYMENT ADD WAREHOUSE_ID CHAR(36) CHARACTER SET ASCII",
        "ALTER TABLE CUSTOMER_PAYMENT ADD CONSTRAINT FK_CUSTOMER_PAYMENT_WAREHOUSE FOREIGN KEY (WAREHOUSE_ID) REFERENCES WAREHOUSE (ID)",
        "CREATE INDEX IX_CUSTOMER_PAYMENT_TILL ON CUSTOMER_PAYMENT (WAREHOUSE_ID, RECEIVED_AT_UTC)",
        "ALTER TABLE CASH_SHIFT ADD CASH_RECEIPTS_RIALS BIGINT CHECK (CASH_RECEIPTS_RIALS IS NULL OR CASH_RECEIPTS_RIALS >= 0)",
    ];
}
