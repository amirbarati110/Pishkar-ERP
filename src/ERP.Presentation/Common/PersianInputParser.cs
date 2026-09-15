using System.Globalization;

namespace ERP.Presentation.Common;

internal static class PersianInputParser
{
    public static bool TryParseDecimal(string? value, out decimal result)
    {
        var normalized = Normalize(value);
        return decimal.TryParse(
            normalized,
            NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,
            out result);
    }

    public static bool TryParseInt64(string? value, out long result)
    {
        var normalized = Normalize(value);
        return long.TryParse(
            normalized,
            NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,
            out result);
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        Span<char> buffer = stackalloc char[value.Length];
        var length = 0;

        foreach (var character in value.Trim())
        {
            if (character is '٬' or ',' or ' ' or '\u00A0')
            {
                continue;
            }

            buffer[length++] = character switch
            {
                >= '۰' and <= '۹' => (char)('0' + character - '۰'),
                >= '٠' and <= '٩' => (char)('0' + character - '٠'),
                '٫' => '.',
                _ => character,
            };
        }

        return new string(buffer[..length]);
    }
}
