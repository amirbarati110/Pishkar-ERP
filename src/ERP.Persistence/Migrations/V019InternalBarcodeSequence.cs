namespace ERP.Persistence.Migrations;

/// <summary>
/// The serial behind «ساخت بارکد خودکار» (checklist «ن-۳»). A sequence, not «max barcode + 1»:
/// two tills asking at the same moment must never be given the same number.
/// </summary>
public sealed class V019InternalBarcodeSequence : IMigration
{
    public int Version => 19;

    public string Name => "Add the sequence for shop-made product barcodes";

    public IReadOnlyList<string> Statements { get; } =
    [
        "CREATE SEQUENCE SEQ_INTERNAL_BARCODE",
    ];
}
