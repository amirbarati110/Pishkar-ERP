namespace ERP.Persistence.Migrations;

/// <summary>
/// Gives completed invoices their human-facing number (see
/// <c>ERP.Domain.Sales.SaleNumber</c>). A Firebird sequence is used on purpose:
/// it is the one transaction-independent object in the engine, so two tills
/// completing at the same moment can never receive the same number, and a
/// rolled-back sale can never cause its number to be handed out again.
/// NUMBER stays nullable because drafts do not have one; the unique index
/// ignores NULLs in Firebird, so any number of drafts can coexist.
/// </summary>
public sealed class V004SaleNumber : IMigration
{
    public int Version => 4;

    public string Name => "Add ascending invoice number to completed sales";

    public IReadOnlyList<string> Statements { get; } =
    [
        "CREATE SEQUENCE SEQ_SALE_NUMBER",
        "ALTER TABLE SALE ADD NUMBER BIGINT CHECK (NUMBER IS NULL OR NUMBER > 0)",
        "CREATE UNIQUE INDEX UX_SALE_NUMBER ON SALE (NUMBER)",
        "CREATE INDEX IX_SALE_COMPLETED_AT ON SALE (COMPLETED_AT_UTC)",
    ];
}
