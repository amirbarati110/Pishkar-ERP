namespace ERP.Domain.Customers;

/// <summary>
/// The check-digit rules of Iran's identity numbers. Both are the published algorithms (checked
/// against known valid numbers in the tests), not guesses — a mistyped digit is caught here
/// instead of on the tax invoice.
/// </summary>
public static class IranianIdentifiers
{
    /// <summary>
    /// کد ملی: 10 digits, not all the same. Digits 1–9 are weighted 10…2, the sum taken modulo 11
    /// as <c>r</c>; the tenth digit must be <c>r</c> when r &lt; 2, else <c>11 − r</c>.
    /// </summary>
    public static bool IsValidNationalCode(string digits)
    {
        if (digits is not { Length: 10 } || !digits.All(char.IsAsciiDigit) || digits.Distinct().Count() == 1)
        {
            return false;
        }

        var sum = 0;
        for (var index = 0; index < 9; index++)
        {
            sum += (digits[index] - '0') * (10 - index);
        }

        var remainder = sum % 11;
        var expected = remainder < 2 ? remainder : 11 - remainder;
        return digits[9] - '0' == expected;
    }

    /// <summary>
    /// شناسه ملی شخص حقوقی: 11 digits. With <c>d = digit10 + 2</c>, the first ten digits are each
    /// added to <c>d</c> and weighted 29, 27, 23, 19, 17 (repeating); the sum modulo 11 (10 counts
    /// as 0) must equal the eleventh digit.
    /// </summary>
    public static bool IsValidLegalId(string digits)
    {
        if (digits is not { Length: 11 } || !digits.All(char.IsAsciiDigit))
        {
            return false;
        }

        ReadOnlySpan<int> weights = [29, 27, 23, 19, 17];
        var offset = (digits[9] - '0') + 2;
        var sum = 0;
        for (var index = 0; index < 10; index++)
        {
            sum += ((digits[index] - '0') + offset) * weights[index % 5];
        }

        var remainder = sum % 11;
        if (remainder == 10)
        {
            remainder = 0;
        }

        return digits[10] - '0' == remainder;
    }
}
