namespace ERP.Persistence.Migrations;

/// <summary>
/// Customer master (source-of-truth §6.4–§6.7). MOBILE is stored normalized
/// (11 Latin digits) and is unique: it is the customer's identity for the
/// duplicate check, and the constraint is what still holds when two tills
/// register the same number at the same moment. The walk-in «مشتری نقدی» is
/// deliberately not a row — a sale with no customer is the cash sale.
/// </summary>
public sealed class V005CreateCustomers : IMigration
{
    public int Version => 5;

    public string Name => "Create customers";

    public IReadOnlyList<string> Statements { get; } =
    [
        """
        CREATE TABLE CUSTOMER (
            ID CHAR(36) CHARACTER SET ASCII NOT NULL PRIMARY KEY,
            NAME VARCHAR(120) CHARACTER SET UTF8 NOT NULL,
            MOBILE CHAR(11) CHARACTER SET ASCII NOT NULL,
            ADDRESS VARCHAR(500) CHARACTER SET UTF8,
            CREDIT_LIMIT_RIALS BIGINT DEFAULT 0 NOT NULL CHECK (CREDIT_LIMIT_RIALS >= 0),
            OPENING_BALANCE_RIALS BIGINT DEFAULT 0 NOT NULL CHECK (OPENING_BALANCE_RIALS >= 0),
            STATUS SMALLINT NOT NULL,
            CONSTRAINT UQ_CUSTOMER_MOBILE UNIQUE (MOBILE)
        )
        """,
        "CREATE INDEX IX_CUSTOMER_NAME ON CUSTOMER (NAME)",
    ];
}
