using System.Globalization;

namespace ERP.Domain.Common;

/// <summary>
/// The one place digits are converted between Persian and Latin forms, so a
/// search box, a duplicate check and a printed receipt can never disagree about
/// what "the same number" is (source-of-truth rule 3).
/// </summary>
public static class PersianNumber
{
    private static readonly char[] PersianDigits = ['۰', '۱', '۲', '۳', '۴', '۵', '۶', '۷', '۸', '۹'];

    /// <summary>
    /// Keeps only the digits of <paramref name="value"/>, mapping Persian (۰-۹)
    /// and Arabic-Indic (٠-٩) forms to Latin ones and dropping everything else —
    /// spaces, dashes, parentheses, letters. Used to reduce a typed identifier
    /// (mobile number, national ID) to the single canonical form that duplicate
    /// checks compare on, because the same number reaches us written many ways.
    /// </summary>
    public static string ToLatinDigitsOnly(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        Span<char> buffer = value.Length <= 64 ? stackalloc char[value.Length] : new char[value.Length];
        var length = 0;

        foreach (var character in value)
        {
            var latin = character switch
            {
                >= '0' and <= '9' => character,
                >= '۰' and <= '۹' => (char)('0' + character - '۰'),
                >= '٠' and <= '٩' => (char)('0' + character - '٠'),
                _ => '\0',
            };

            if (latin != '\0')
            {
                buffer[length++] = latin;
            }
        }

        return new string(buffer[..length]);
    }

    /// <summary>
    /// A whole amount with thousands separators in Persian digits —
    /// «۱۲,۵۰۰,۰۰۰», the form source-of-truth §3.4 gives for money on screen.
    /// </summary>
    public static string FormatGrouped(long value)
    {
        return ToPersianDigits(value.ToString("#,0", CultureInfo.InvariantCulture));
    }

    public static string Format(decimal value)
    {
        return ToPersianDigits(value.ToString("G29", CultureInfo.InvariantCulture));
    }

    private static string ToPersianDigits(string invariant)
    {
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

