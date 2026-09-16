namespace ERP.Persistence.Migrations;

/// <summary>
/// «توضیحات فاکتور» (approved sales-screen design, user feedback after seeing
/// the screen: «مبالغ بیاد سمت چپ + توضیحات») — a free-text field on the
/// invoice itself, not on a line. Nullable: most invoices have nothing to say.
/// </summary>
public sealed class V007SaleNote : IMigration
{
    public int Version => 7;

    public string Name => "Add a free-text note to the invoice";

    public IReadOnlyList<string> Statements { get; } =
    [
        "ALTER TABLE SALE ADD NOTE VARCHAR(500) CHARACTER SET UTF8",
    ];
}
