namespace ERP.Persistence.Migrations;

public sealed class V002CreateSales : IMigration
{
    public int Version => 2;

    public string Name => "Create sales (POS cart/invoice) tables";

    public IReadOnlyList<string> Statements { get; } =
    [
        """
        CREATE TABLE SALE (
            ID CHAR(36) CHARACTER SET ASCII NOT NULL PRIMARY KEY,
            WAREHOUSE_ID CHAR(36) CHARACTER SET ASCII NOT NULL,
            CUSTOMER_ID CHAR(36) CHARACTER SET ASCII,
            STATUS SMALLINT NOT NULL,
            DISCOUNT_RIALS BIGINT NOT NULL CHECK (DISCOUNT_RIALS >= 0),
            PAYMENT_METHOD SMALLINT,
            OPENED_AT_UTC TIMESTAMP NOT NULL,
            COMPLETED_AT_UTC TIMESTAMP
        )
        """,
        """
        CREATE TABLE SALE_LINE (
            SALE_ID CHAR(36) CHARACTER SET ASCII NOT NULL,
            PRODUCT_ID CHAR(36) CHARACTER SET ASCII NOT NULL,
            QUANTITY DECIMAL(18, 3) NOT NULL CHECK (QUANTITY > 0),
            UNIT_PRICE_RIALS BIGINT NOT NULL CHECK (UNIT_PRICE_RIALS >= 0),
            CONSTRAINT PK_SALE_LINE PRIMARY KEY (SALE_ID, PRODUCT_ID),
            CONSTRAINT FK_SALE_LINE_SALE FOREIGN KEY (SALE_ID) REFERENCES SALE (ID)
        )
        """,
        "CREATE INDEX IX_SALE_STATUS ON SALE (STATUS)",
        "CREATE INDEX IX_SALE_WAREHOUSE ON SALE (WAREHOUSE_ID)",
    ];
}
