namespace ERP.Persistence.Migrations;

/// <summary>
/// Fills the columns V016 added for customers that already exist: their single name becomes the
/// first name (kind «حقیقی» is the column default), and each gets the next customer number (in no particular
/// order). Later customers get theirs from the store when they are created.
/// </summary>
public sealed class V017CustomerProfileBackfill : IMigration
{
    public int Version => 17;

    public string Name => "Backfill customer first name and customer number";

    public IReadOnlyList<string> Statements { get; } =
    [
        "UPDATE CUSTOMER SET FIRST_NAME = NAME WHERE FIRST_NAME IS NULL",
        "UPDATE CUSTOMER SET CODE = NEXT VALUE FOR SEQ_CUSTOMER_CODE WHERE CODE IS NULL",
    ];
}
