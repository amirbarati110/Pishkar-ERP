namespace ERP.Persistence.Migrations;

/// <summary>
/// Who can sign in (milestone-1 build order step 4, source-of-truth §15.24
/// Phase 0). USERNAME is unique and stored already-lowercased by
/// <c>User.Create</c> — the constraint is what still holds when two setup
/// screens somehow race to claim the same name. PASSWORD_HASH is the
/// self-describing PBKDF2 string (<c>ERP.Domain.Identity.PasswordHash</c>),
/// never the plain password.
/// </summary>
public sealed class V009CreateAppUser : IMigration
{
    public int Version => 9;

    public string Name => "Create app user";

    public IReadOnlyList<string> Statements { get; } =
    [
        """
        CREATE TABLE APP_USER (
            ID CHAR(36) CHARACTER SET ASCII NOT NULL PRIMARY KEY,
            USERNAME VARCHAR(40) CHARACTER SET ASCII NOT NULL,
            DISPLAY_NAME VARCHAR(80) CHARACTER SET UTF8 NOT NULL,
            PASSWORD_HASH VARCHAR(200) CHARACTER SET ASCII NOT NULL,
            ROLE SMALLINT NOT NULL,
            STATUS SMALLINT NOT NULL,
            CONSTRAINT UQ_APP_USER_USERNAME UNIQUE (USERNAME)
        )
        """,
    ];
}
