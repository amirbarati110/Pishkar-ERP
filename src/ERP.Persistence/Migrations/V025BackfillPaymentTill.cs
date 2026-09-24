namespace ERP.Persistence.Migrations;

/// <summary>
/// Every payment recorded before V024 was taken at the sales screen, which has only ever worked on
/// the main warehouse — so that is their till. Without this, a shift already open during the
/// upgrade would stop expecting cash it did receive.
/// </summary>
public sealed class V025BackfillPaymentTill : IMigration
{
    public int Version => 25;

    public string Name => "Give existing customer payments the main warehouse's till";

    public IReadOnlyList<string> Statements { get; } =
    [
        "UPDATE CUSTOMER_PAYMENT SET WAREHOUSE_ID = '33333333-3333-4333-8333-333333333333' WHERE WAREHOUSE_ID IS NULL",
    ];
}
