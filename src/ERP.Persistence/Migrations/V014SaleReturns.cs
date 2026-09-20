namespace ERP.Persistence.Migrations;

/// <summary>
/// «مرجوعی» (§6.25). Three pieces the feature cannot work without:
/// what each completed sale line actually cost (stock only remembers what is
/// left in each layer, not which layers a sale drew from, so a return could
/// otherwise not put goods back at the cost they left at), the return
/// document itself, and the cash refunded during a shift (a drawer's
/// expected cash falls when money is handed back).
/// </summary>
public sealed class V014SaleReturns : IMigration
{
    public int Version => 14;

    public string Name => "Add sale returns, per-line sale cost, and cash refunds on shifts";

    public IReadOnlyList<string> Statements { get; } =
    [
        """
        CREATE TABLE SALE_LINE_COST (
            SALE_ID CHAR(36) CHARACTER SET ASCII NOT NULL,
            PRODUCT_ID CHAR(36) CHARACTER SET ASCII NOT NULL,
            COST_RIALS BIGINT NOT NULL CHECK (COST_RIALS >= 0),
            COSTED_QTY DECIMAL(18, 3) NOT NULL CHECK (COSTED_QTY > 0),
            CONSTRAINT PK_SALE_LINE_COST PRIMARY KEY (SALE_ID, PRODUCT_ID),
            CONSTRAINT FK_SALE_LINE_COST_SALE FOREIGN KEY (SALE_ID) REFERENCES SALE (ID)
        )
        """,
        "CREATE SEQUENCE SEQ_RETURN_NUMBER",
        """
        CREATE TABLE SALE_RETURN (
            ID CHAR(36) CHARACTER SET ASCII NOT NULL PRIMARY KEY,
            SALE_ID CHAR(36) CHARACTER SET ASCII NOT NULL,
            WAREHOUSE_ID CHAR(36) CHARACTER SET ASCII NOT NULL,
            CUSTOMER_ID CHAR(36) CHARACTER SET ASCII,
            NUMBER BIGINT NOT NULL CHECK (NUMBER > 0),
            REFUND_METHOD SMALLINT NOT NULL,
            REASON VARCHAR(300) CHARACTER SET UTF8 NOT NULL,
            COMPLETED_AT_UTC TIMESTAMP NOT NULL,
            CONSTRAINT FK_SALE_RETURN_SALE FOREIGN KEY (SALE_ID) REFERENCES SALE (ID)
        )
        """,
        """
        CREATE TABLE SALE_RETURN_LINE (
            RETURN_ID CHAR(36) CHARACTER SET ASCII NOT NULL,
            PRODUCT_ID CHAR(36) CHARACTER SET ASCII NOT NULL,
            QUANTITY DECIMAL(18, 3) NOT NULL CHECK (QUANTITY > 0),
            DISPOSITION SMALLINT NOT NULL,
            NET_RIALS BIGINT NOT NULL CHECK (NET_RIALS >= 0),
            TAX_RIALS BIGINT NOT NULL CHECK (TAX_RIALS >= 0),
            UNIT_COST_RIALS BIGINT CHECK (UNIT_COST_RIALS IS NULL OR UNIT_COST_RIALS >= 0),
            CONSTRAINT PK_SALE_RETURN_LINE PRIMARY KEY (RETURN_ID, PRODUCT_ID),
            CONSTRAINT FK_SALE_RETURN_LINE_RETURN FOREIGN KEY (RETURN_ID) REFERENCES SALE_RETURN (ID)
        )
        """,
        "CREATE UNIQUE INDEX UX_SALE_RETURN_NUMBER ON SALE_RETURN (NUMBER)",
        "CREATE INDEX IX_SALE_RETURN_SALE ON SALE_RETURN (SALE_ID)",
        "CREATE INDEX IX_SALE_RETURN_WAREHOUSE_AT ON SALE_RETURN (WAREHOUSE_ID, COMPLETED_AT_UTC)",
        "ALTER TABLE CASH_SHIFT ADD CASH_REFUNDS_RIALS BIGINT CHECK (CASH_REFUNDS_RIALS IS NULL OR CASH_REFUNDS_RIALS >= 0)",
    ];
}
