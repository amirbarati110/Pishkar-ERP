namespace ERP.Persistence.Migrations;

/// <summary>
/// §2.8 Performance Budget: <c>SALE_LINE</c>'s only index was its primary key
/// (SALE_ID, PRODUCT_ID) — useless for "every line that sold this product",
/// which the sales screen's product list (§6.8 «پرفروش‌ها») runs once per
/// product on every page load. Found by <c>PerformanceBudgetTests</c> with a
/// realistic product/sale volume: that query alone made the whole list take
/// ~2s against the §2.8 target of &lt;200ms.
/// </summary>
public sealed class V013SaleLineProductIndex : IMigration
{
    public int Version => 13;

    public string Name => "Index SALE_LINE by product for the sales screen's product list";

    public IReadOnlyList<string> Statements { get; } =
    [
        "CREATE INDEX IX_SALE_LINE_PRODUCT ON SALE_LINE (PRODUCT_ID)",
    ];
}
