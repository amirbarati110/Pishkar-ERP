using ERP.Domain.Common;

namespace ERP.Domain.Sales;

/// <summary>
/// The number the cashier and the customer both refer to an invoice by — the
/// «شماره فاکتور ۱۲۵۸» on the printed receipt, as opposed to
/// <see cref="SaleId"/>, which is the internal key nobody reads aloud.
///
/// <para><b>Why a plain ascending counter and not a «۱۴۰۵-۱۲۰۱» style number:</b>
/// under Iran's سامانه مودیان the internal invoice serial is one component of the
/// شماره منحصر به فرد مالیاتی and must be issued unique and strictly ascending;
/// اطلاعیه ۳۱ of the tax authority states explicitly that it must <b>not</b> restart
/// at the beginning of a new fiscal year (last of 1403 = 2000 → first of 1404 =
/// 2001). A year-prefixed number that resets each year — the habit carried over
/// from older local software such as هلو — would be rejected there. So the
/// number carries no year, no branch and no meaning beyond its order.</para>
///
/// <para>Gaps are legitimate and expected: a number is drawn from a Firebird
/// sequence, which is transaction-independent by design, so a sale that fails
/// after drawing one leaves a hole rather than handing the same number to the
/// next invoice. Ascending is the guarantee; contiguous is not.</para>
/// </summary>
public readonly record struct SaleNumber : IComparable<SaleNumber>
{
    private SaleNumber(long value)
    {
        Value = value;
    }

    public long Value { get; }

    public static SaleNumber From(long value)
    {
        if (value <= 0)
        {
            throw new DomainException("شماره فاکتور باید بزرگ‌تر از صفر باشد.");
        }

        return new SaleNumber(value);
    }

    public int CompareTo(SaleNumber other) => Value.CompareTo(other.Value);

    public static bool operator <(SaleNumber left, SaleNumber right) => left.CompareTo(right) < 0;

    public static bool operator <=(SaleNumber left, SaleNumber right) => left.CompareTo(right) <= 0;

    public static bool operator >(SaleNumber left, SaleNumber right) => left.CompareTo(right) > 0;

    public static bool operator >=(SaleNumber left, SaleNumber right) => left.CompareTo(right) >= 0;

    /// <summary>Latin digits — for logs, keys and the audit trail.</summary>
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Persian digits — what goes on screen and on the printed receipt (§3.1).</summary>
    public string ToPersianString() => PersianNumber.Format(Value);
}
