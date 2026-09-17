namespace ERP.Persistence.Migrations;

/// <summary>«Backup محلی قابل اعتبارسنجی» (milestone-1 build order step 11). Rows here describe .fbk files that live outside the database; deleting the file never cascades into deleting the row — the history stays even for a backup someone later removed from disk.</summary>
public sealed class V012CreateBackupRecord : IMigration
{
    public int Version => 12;

    public string Name => "Create backup record";

    public IReadOnlyList<string> Statements { get; } =
    [
        """
        CREATE TABLE BACKUP_RECORD (
            ID CHAR(36) CHARACTER SET ASCII NOT NULL PRIMARY KEY,
            FILE_PATH VARCHAR(500) CHARACTER SET UTF8 NOT NULL,
            SIZE_BYTES BIGINT NOT NULL CHECK (SIZE_BYTES > 0),
            CREATED_AT_UTC TIMESTAMP NOT NULL,
            STATUS SMALLINT NOT NULL,
            VERIFIED_AT_UTC TIMESTAMP,
            VERIFICATION_NOTE VARCHAR(500) CHARACTER SET UTF8
        )
        """,
        "CREATE INDEX IX_BACKUP_RECORD_CREATED ON BACKUP_RECORD (CREATED_AT_UTC)",
    ];
}
