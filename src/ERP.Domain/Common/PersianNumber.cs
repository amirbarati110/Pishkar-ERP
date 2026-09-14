using System.Globalization;

namespace ERP.Domain.Common;

internal static class PersianNumber
{
    private static readonly char[] PersianDigits = ['۰', '۱', '۲', '۳', '۴', '۵', '۶', '۷', '۸', '۹'];

    public static string Format(decimal value)
    {
        var invariant = value.ToString("G29", CultureInfo.InvariantCulture);
        return string.Create(invariant.Length, invariant, static (span, source) =>
        {
            for (var index = 0; index < source.Length; index++)
            {
                var character = source[index];
                span[index] = character is >= '0' and <= '9'
                    ? PersianDigits[character - '0']
                    : character;
            }
        });
    }
}

