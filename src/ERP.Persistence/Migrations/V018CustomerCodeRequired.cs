namespace ERP.Persistence.Migrations;

/// <summary>Every customer has a number now (V017), so it can be required and unique.</summary>
public sealed class V018CustomerCodeRequired : IMigration
{
    public int Version => 18;

    public string Name => "Require a unique customer number";

    public IReadOnlyList<string> Statements { get; } =
    [
        "ALTER TABLE CUSTOMER ALTER COLUMN CODE SET NOT NULL",
        "ALTER TABLE CUSTOMER ADD CONSTRAINT UQ_CUSTOMER_CODE UNIQUE (CODE)",
    ];
}
