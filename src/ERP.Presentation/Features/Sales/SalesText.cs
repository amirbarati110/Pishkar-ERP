using ERP.Domain.Common;
using ERP.Domain.Sales;
using ERP.Presentation.Common;

namespace ERP.Presentation.Features.Sales;

/// <summary>
/// How the sales screen writes numbers and reads them back. Money is stored in
/// Rials and shown in Tomans with Persian digits and thousands separators
/// (source-of-truth §3.4); every conversion goes through here so the screen
/// has one rule, not one per text box.
/// </summary>
public static class SalesText
{
    private const long RialsPerToman = 10;

    /// <summary>«۱,۲۵۰,۰۰۰» — whole Tomans. A total can carry odd Rials from tax rounding; it is shown to the nearest Toman.</summary>
    public static string Tomans(Money amount) => Tomans(amount.Rials);

    public static string Tomans(long rials)
    {
        var tomans = (long)Math.Round(rials / (decimal)RialsPerToman, 0, MidpointRounding.AwayFromZero);
        return PersianNumber.FormatGrouped(tomans);
    }

    public static string Quantity(decimal value) => PersianNumber.Format(value);

    public static string Percent(decimal value) => PersianNumber.Format(Math.Round(value, 2));

    /// <summary>Reads «۱۲۵٬۰۰۰» / «125,000» typed as Tomans. Blank is zero; anything else invalid is null.</summary>
    public static long? ParseTomansToRials(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        return PersianInputParser.TryParseInt64(text, out var tomans) && tomans >= 0
            ? checked(tomans * RialsPerToman)
            : null;
    }

    public static decimal? ParseDecimal(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        return PersianInputParser.TryParseDecimal(text, out var value) && value >= 0 ? value : null;
    }

    public static string PaymentMethodName(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "نقدی",
        PaymentMethod.Card => "کارتخوان",
        PaymentMethod.Credit => "نسیه",
        PaymentMethod.Cheque => "چک",
        _ => string.Empty,
    };
}
