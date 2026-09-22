using ERP.Domain.Catalog;
using ERP.Domain.Common;

namespace ERP.Domain.Tests.Catalog;

/// <summary>«ساخت بارکد خودکار» (checklist «ن-۳»): a shop-made EAN-13 in GS1's 040–049 range.</summary>
public sealed class InternalBarcodeTests
{
    [Fact]
    public void ASerialBecomesA13DigitCodeStartingWith04()
    {
        var barcode = InternalBarcode.FromSerial(1);

        Assert.Equal(13, barcode.Length);
        Assert.StartsWith("04", barcode, StringComparison.Ordinal);
        Assert.Equal("0400000000015", barcode); // 04 + 0000000001 + check digit 5
    }

    [Fact]
    public void EverySerialMakesAValidEan13()
    {
        foreach (var serial in new long[] { 1, 42, 1_000_000, InternalBarcode.MaximumSerial })
        {
            var barcode = InternalBarcode.FromSerial(serial);
            Assert.True(InternalBarcode.IsValidEan13(barcode));
            Assert.True(InternalBarcode.IsInternal(barcode));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ASerialOutsideTheRangeIsRejected(long serial) =>
        Assert.Throws<DomainException>(() => InternalBarcode.FromSerial(serial));

    [Fact]
    public void ASerialBeyondTheMaximumIsRejected() =>
        Assert.Throws<DomainException>(() => InternalBarcode.FromSerial(InternalBarcode.MaximumSerial + 1));

    [Theory]
    [InlineData("6260000002001")] // a real manufacturer prefix, not the shop's own
    [InlineData("123")]
    [InlineData(null)]
    [InlineData("040000000001A")]
    public void ACodeThatIsNotAValidInternalBarcodeIsRejected(string? barcode) =>
        Assert.False(InternalBarcode.IsInternal(barcode));

    [Fact]
    public void AWrongCheckDigitFailsValidation()
    {
        var barcode = InternalBarcode.FromSerial(1);
        var tampered = barcode[..12] + (barcode[12] == '9' ? '0' : (char)(barcode[12] + 1));

        Assert.False(InternalBarcode.IsValidEan13(tampered));
    }
}
