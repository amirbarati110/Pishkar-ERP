namespace ERP.Persistence.Migrations;

/// <summary>
/// What a customer account is computed from (<c>ERP.Domain.Customers.CustomerAccount</c>):
/// the tax and payable total fixed on each completed invoice, and payments
/// received. TAX_RIALS/TOTAL_RIALS stay NULL for drafts and for invoices
/// completed before this migration — their tax rate was never stored, so a
/// value cannot be reconstructed honestly. None of those older invoices had a
/// customer (customers did not exist yet), so no account is affected.
/// </summary>
public sealed class V006CustomerAccount : IMigration
{
    public int Version => 6;

    public string Name => "Record invoice totals and customer payments";

    public IReadOnlyList<string> Statements { get; } =
    [
        "ALTER TABLE SALE ADD TAX_RIALS BIGINT CHECK (TAX_RIALS IS NULL OR TAX_RIALS >= 0)",
        "ALTER TABLE SALE ADD TOTAL_RIALS BIGINT CHECK (TOTAL_RIALS IS NULL OR TOTAL_RIALS >= 0)",
        // The foreign key also gives Firebird the index the account query needs.
        "ALTER TABLE SALE ADD CONSTRAINT FK_SALE_CUSTOMER FOREIGN KEY (CUSTOMER_ID) REFERENCES CUSTOMER (ID)",
        """
        CREATE TABLE CUSTOMER_PAYMENT (
            ID CHAR(36) CHARACTER SET ASCII NOT NULL PRIMARY KEY,
            CUSTOMER_ID CHAR(36) CHARACTER SET ASCII NOT NULL,
            AMOUNT_RIALS BIGINT NOT NULL CHECK (AMOUNT_RIALS > 0),
            METHOD SMALLINT NOT NULL,
            NOTE VARCHAR(250) CHARACTER SET UTF8,
            RECEIVED_AT_UTC TIMESTAMP NOT NULL,
            CONSTRAINT FK_CUSTOMER_PAYMENT_CUSTOMER FOREIGN KEY (CUSTOMER_ID) REFERENCES CUSTOMER (ID)
        )
        """,
        "CREATE INDEX IX_CUSTOMER_PAYMENT_CUSTOMER ON CUSTOMER_PAYMENT (CUSTOMER_ID)",
    ];
}
