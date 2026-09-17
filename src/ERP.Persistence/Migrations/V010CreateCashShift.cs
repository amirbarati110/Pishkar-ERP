namespace ERP.Persistence.Migrations;

/// <summary>
/// «شیفت صندوق پایه» (milestone-1 build order step 8). COUNTED_CASH_RIALS,
/// CASH_SALES_RIALS, CLOSED_AT_UTC and CLOSED_BY_USER_ID all stay NULL until
/// the shift is closed — a still-open shift has no reconciliation yet, and
/// that is a fact worth keeping distinct from "counted zero".
/// </summary>
public sealed class V010CreateCashShift : IMigration
{
    public int Version => 10;

    public string Name => "Create cash shift";

    public IReadOnlyList<string> Statements { get; } =
    [
        """
        CREATE TABLE CASH_SHIFT (
            ID CHAR(36) CHARACTER SET ASCII NOT NULL PRIMARY KEY,
            WAREHOUSE_ID CHAR(36) CHARACTER SET ASCII NOT NULL,
            OPENED_BY_USER_ID CHAR(36) CHARACTER SET ASCII NOT NULL,
            OPENED_AT_UTC TIMESTAMP NOT NULL,
            OPENING_CASH_RIALS BIGINT NOT NULL CHECK (OPENING_CASH_RIALS >= 0),
            STATUS SMALLINT NOT NULL,
            CLOSED_BY_USER_ID CHAR(36) CHARACTER SET ASCII,
            CLOSED_AT_UTC TIMESTAMP,
            COUNTED_CASH_RIALS BIGINT CHECK (COUNTED_CASH_RIALS IS NULL OR COUNTED_CASH_RIALS >= 0),
            CASH_SALES_RIALS BIGINT CHECK (CASH_SALES_RIALS IS NULL OR CASH_SALES_RIALS >= 0),
            NOTE VARCHAR(500) CHARACTER SET UTF8
        )
        """,
        "CREATE INDEX IX_CASH_SHIFT_WAREHOUSE_STATUS ON CASH_SHIFT (WAREHOUSE_ID, STATUS)",
    ];
}
