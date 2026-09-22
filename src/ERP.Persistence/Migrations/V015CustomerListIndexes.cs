namespace ERP.Persistence.Migrations;

/// <summary>
/// «لیست مشتریان» shows every customer's balance, which adds up that customer's
/// credit refunds. <c>SALE_RETURN.CUSTOMER_ID</c> had no index (unlike SALE and
/// CUSTOMER_PAYMENT, whose foreign keys carry one), so each row of the list would
/// scan every return.
/// </summary>
public sealed class V015CustomerListIndexes : IMigration
{
    public int Version => 15;

    public string Name => "Index SALE_RETURN by customer for the customer list balance";

    public IReadOnlyList<string> Statements { get; } =
    [
        "CREATE INDEX IX_SALE_RETURN_CUSTOMER ON SALE_RETURN (CUSTOMER_ID)",
    ];
}
