namespace ERP.Persistence.Migrations;

/// <summary>
/// A return may give back the invoice's «خدمات/هزینه» and its VAT when the cashier chooses so
/// (user decision 1405/07/02). It has no goods line, so it is stored on the return itself; zero
/// for every return made before this (they never refunded it).
///
/// <para>The partial unique index makes «at most once per invoice» hold even when two tills
/// return from the same invoice at the same moment.</para>
/// </summary>
public sealed class V023SaleReturnServiceCharge : IMigration
{
    public int Version => 23;

    public string Name => "Let a return give back the invoice's service charge";

    public IReadOnlyList<string> Statements { get; } =
    [
        "ALTER TABLE SALE_RETURN ADD SERVICE_NET_RIALS BIGINT DEFAULT 0 NOT NULL CHECK (SERVICE_NET_RIALS >= 0)",
        "ALTER TABLE SALE_RETURN ADD SERVICE_TAX_RIALS BIGINT DEFAULT 0 NOT NULL CHECK (SERVICE_TAX_RIALS >= 0)",
        "CREATE UNIQUE INDEX UX_SALE_RETURN_SERVICE_ONCE ON SALE_RETURN (SALE_ID) WHERE SERVICE_NET_RIALS > 0",
    ];
}
