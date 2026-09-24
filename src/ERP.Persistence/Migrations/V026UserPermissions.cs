namespace ERP.Persistence.Migrations;

/// <summary>
/// The four rights a manager switches on or off per cashier (<c>CashierPermissions</c> as bit flags;
/// user decision 1405/07/02). Every existing user gets all four — nothing a cashier could do before
/// the upgrade is taken away by it.
/// </summary>
public sealed class V026UserPermissions : IMigration
{
    public int Version => 26;

    public string Name => "Store each cashier's switchable permissions";

    public IReadOnlyList<string> Statements { get; } =
    [
        "ALTER TABLE APP_USER ADD PERMISSIONS SMALLINT DEFAULT 15 NOT NULL CHECK (PERMISSIONS BETWEEN 0 AND 15)",
    ];
}
