namespace ERP.Persistence.Migrations;

/// <summary>
/// «صورتحساب اصلاحی» (source-of-truth §10.10 / Codex rule 15): CORRECTS_SALE_ID
/// points a correction invoice at the sale it stands in for. Self-referencing
/// FK on purpose — Firebird then enforces that it always names a real sale.
/// Nullable: almost every sale is not a correction of anything.
/// </summary>
public sealed class V008SaleCorrection : IMigration
{
    public int Version => 8;

    public string Name => "Add the correction-invoice link to sales";

    public IReadOnlyList<string> Statements { get; } =
    [
        "ALTER TABLE SALE ADD CORRECTS_SALE_ID CHAR(36) CHARACTER SET ASCII",
        "ALTER TABLE SALE ADD CONSTRAINT FK_SALE_CORRECTS FOREIGN KEY (CORRECTS_SALE_ID) REFERENCES SALE (ID)",
        // Both directions matter: finding a correction's original (WHERE ID = ...
        // for the FK itself, already indexed as the primary key) and finding an
        // original's correction (WHERE CORRECTS_SALE_ID = ...), which is the
        // lookup ListCompletedAsync and the customer ledger run on every read.
        "CREATE INDEX IX_SALE_CORRECTS ON SALE (CORRECTS_SALE_ID)",
    ];
}
