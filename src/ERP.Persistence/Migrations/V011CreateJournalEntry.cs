namespace ERP.Persistence.Migrations;

/// <summary>«سند حسابداری حداقلی» (milestone-1 §10.5/§6.23) — auto-posted, never edited; there is no UPDATE path for either table.</summary>
public sealed class V011CreateJournalEntry : IMigration
{
    public int Version => 11;

    public string Name => "Create journal entry (minimal auto accounting)";

    public IReadOnlyList<string> Statements { get; } =
    [
        """
        CREATE TABLE JOURNAL_ENTRY (
            ID CHAR(36) CHARACTER SET ASCII NOT NULL PRIMARY KEY,
            SOURCE_TYPE SMALLINT NOT NULL,
            SOURCE_ID VARCHAR(64) CHARACTER SET ASCII NOT NULL,
            POSTED_AT_UTC TIMESTAMP NOT NULL
        )
        """,
        """
        CREATE TABLE JOURNAL_LINE (
            ENTRY_ID CHAR(36) CHARACTER SET ASCII NOT NULL,
            LINE_NO SMALLINT NOT NULL,
            ACCOUNT SMALLINT NOT NULL,
            DEBIT_RIALS BIGINT NOT NULL CHECK (DEBIT_RIALS >= 0),
            CREDIT_RIALS BIGINT NOT NULL CHECK (CREDIT_RIALS >= 0),
            CONSTRAINT PK_JOURNAL_LINE PRIMARY KEY (ENTRY_ID, LINE_NO),
            CONSTRAINT FK_JOURNAL_LINE_ENTRY FOREIGN KEY (ENTRY_ID) REFERENCES JOURNAL_ENTRY (ID)
        )
        """,
        "CREATE INDEX IX_JOURNAL_ENTRY_SOURCE ON JOURNAL_ENTRY (SOURCE_TYPE, SOURCE_ID)",
    ];
}
