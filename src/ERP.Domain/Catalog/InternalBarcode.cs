using ERP.Domain.Common;

namespace ERP.Domain.Catalog;

/// <summary>
/// The barcodes the shop makes itself for products that arrive without one (§9.5 «Internal
/// Barcode», checklist «ن-۳»). EAN-13, so any scanner and label printer reads them.
///
/// <para><b>Why the prefix is 04.</b> GS1 keeps the numbers 040–049 for «restricted circulation
/// within a company» — codes a shop may make for its own use and that no manufacturer can ever
/// hold. The other reserved range, 20–29, is what price/weight scales print on their labels
/// (2X + product code + weight or price), so a shop barcode there could be misread as a weighed
/// item. The source document named prefix 2; it was moved to 04 for exactly that reason
/// (recorded in the checklist).</para>
///
/// <para>Layout: <c>04</c> + a 10-digit serial (zero-padded) + the EAN-13 check digit — 13 digits.
/// The serial comes from a database sequence, so two tills never make the same code.</para>
/// </summary>
public static class InternalBarcode
{
    public const string Prefix = "04";

    /// <summary>Ten serial digits: about ten billion codes, far beyond any shop.</summary>
    public const long MaximumSerial = 9_999_999_999;

    private const int SerialDigits = 10;

    public static string FromSerial(long serial)
    {
        if (serial is < 1 or > MaximumSerial)
        {
            throw new DomainException("شماره‌ی ترتیبی بارکد داخلی خارج از محدوده است.");
        }

        var body = Prefix + serial.ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(SerialDigits, '0');
        return body + CheckDigit(body);
    }

    /// <summary>True for a valid 13-digit EAN-13 in the shop's own range — a code this shop made.</summary>
    public static bool IsInternal(string? barcode) =>
        IsValidEan13(barcode) && barcode!.StartsWith(Prefix, StringComparison.Ordinal);

    public static bool IsValidEan13(string? barcode) =>
        barcode is { Length: 13 }
        && barcode.All(char.IsAsciiDigit)
        && CheckDigit(barcode[..12]) == barcode[12];

    /// <summary>The EAN-13 check digit of the first 12 digits: odd places count once, even places three times.</summary>
    public static char CheckDigit(string first12Digits)
    {
        var sum = 0;
        for (var index = 0; index < 12; index++)
        {
            sum += (first12Digits[index] - '0') * (index % 2 == 0 ? 1 : 3);
        }

        return (char)('0' + ((10 - (sum % 10)) % 10));
    }
}
