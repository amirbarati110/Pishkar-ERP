namespace ERP.Persistence.Migrations;

/// <summary>
/// Adds what the cashier screen needs on an invoice beyond plain line items:
/// a row discount and the catalogue price it was sold against (§6.10/§6.11),
/// and an invoice-level service charge — "خدمات / هزینه"، the «ارسال» line in
/// common Iranian POS software.
/// </summary>
public sealed class V003SaleLineDiscountAndServiceCharge : IMigration
{
    public int Version => 3;

    public string Name => "Add line discount, catalog price and invoice service charge";

    public IReadOnlyList<string> Statements { get; } =
    [
        "ALTER TABLE SALE_LINE ADD DISCOUNT_RIALS BIGINT DEFAULT 0 NOT NULL",
        "ALTER TABLE SALE_LINE ADD CATALOG_PRICE_RIALS BIGINT DEFAULT 0 NOT NULL",
        "ALTER TABLE SALE ADD SERVICE_CHARGE_RIALS BIGINT DEFAULT 0 NOT NULL",
    ];
}
