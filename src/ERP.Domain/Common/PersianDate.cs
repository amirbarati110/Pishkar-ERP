using System.Globalization;

namespace ERP.Domain.Common;

/// <summary>
/// A calendar day in the Iranian (Solar Hijri) calendar, as the business sees
/// it — «۱۴۰۵/۰۶/۲۳» — per source-of-truth §3.2: screens, filters and reports
/// work in Persian dates, while storage keeps UTC timestamps.
///
/// <para><b>The day is Tehran's day, not UTC's.</b> Iran is UTC+3:30, so an
/// invoice issued at 00:30 Tehran time carries a UTC timestamp from the
/// previous date. Every «امروز» filter must convert through
/// <see cref="StartUtc"/>/<see cref="EndUtc"/>, never compare UTC dates.</para>
///
/// <para>The offset comes from the operating system's time zone database, not a
/// hard-coded +03:30: Iran dropped daylight saving time in 2022 and the rule
/// has changed before, so a future change should arrive with a Windows update
/// rather than a code change. +03:30 is used only if the machine has no Iran
/// time zone at all.</para>
/// </summary>
public readonly record struct PersianDate : IComparable<PersianDate>
{
    private static readonly PersianCalendar Calendar = new();

    private static readonly Lazy<TimeZoneInfo> IranTimeZone = new(FindIranTimeZone);

    private PersianDate(int year, int month, int day)
    {
        Year = year;
        Month = month;
        Day = day;
    }

    public int Year { get; }

    public int Month { get; }

    public int Day { get; }

    /// <summary>The moment this day begins in Tehran, as UTC (inclusive).</summary>
    public DateTimeOffset StartUtc => ToUtc(Year, Month, Day);

    /// <summary>The moment the next day begins in Tehran, as UTC (exclusive).</summary>
    public DateTimeOffset EndUtc => AddDays(1).StartUtc;

    public static PersianDate Create(int year, int month, int day)
    {
        if (year is < 1 or > 9378 || month is < 1 or > 12 || day < 1 || day > Calendar.GetDaysInMonth(year, month))
        {
            throw new DomainException("تاریخ شمسی معتبر نیست.");
        }

        return new PersianDate(year, month, day);
    }

    /// <summary>The Tehran calendar day that contains <paramref name="moment"/>.</summary>
    public static PersianDate FromUtc(DateTimeOffset moment)
    {
        var local = TimeZoneInfo.ConvertTime(moment, IranTimeZone.Value).DateTime;
        return new PersianDate(Calendar.GetYear(local), Calendar.GetMonth(local), Calendar.GetDayOfMonth(local));
    }

    /// <summary>The Tehran wall-clock time of <paramref name="moment"/> — for showing «۱۰:۲۴» next to an invoice.</summary>
    public static TimeOnly TimeOfDayInIran(DateTimeOffset moment)
    {
        return TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(moment, IranTimeZone.Value).DateTime);
    }

    public DayOfWeek DayOfWeek => Calendar.ToDateTime(Year, Month, Day, 0, 0, 0, 0).DayOfWeek;

    public PersianDate AddDays(int days)
    {
        var shifted = Calendar.ToDateTime(Year, Month, Day, 0, 0, 0, 0).AddDays(days);
        return new PersianDate(Calendar.GetYear(shifted), Calendar.GetMonth(shifted), Calendar.GetDayOfMonth(shifted));
    }

    public int CompareTo(PersianDate other) => (Year, Month, Day).CompareTo((other.Year, other.Month, other.Day));

    public static bool operator <(PersianDate left, PersianDate right) => left.CompareTo(right) < 0;

    public static bool operator <=(PersianDate left, PersianDate right) => left.CompareTo(right) <= 0;

    public static bool operator >(PersianDate left, PersianDate right) => left.CompareTo(right) > 0;

    public static bool operator >=(PersianDate left, PersianDate right) => left.CompareTo(right) >= 0;

    /// <summary>«۱۴۰۵/۰۶/۲۳» — Persian digits, two-digit month and day, as on the reference screens.</summary>
    public override string ToString()
    {
        return PersianNumber.DigitsToPersian(
            string.Create(CultureInfo.InvariantCulture, $"{Year:0000}/{Month:00}/{Day:00}"));
    }

    private static DateTimeOffset ToUtc(int year, int month, int day)
    {
        var localMidnight = Calendar.ToDateTime(year, month, day, 0, 0, 0, 0);
        var offset = IranTimeZone.Value.GetUtcOffset(localMidnight);
        return new DateTimeOffset(localMidnight, offset).ToUniversalTime();
    }

    private static TimeZoneInfo FindIranTimeZone()
    {
        foreach (var id in new[] { "Iran Standard Time", "Asia/Tehran" })
        {
            if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone))
            {
                return zone;
            }
        }

        return TimeZoneInfo.CreateCustomTimeZone("Iran Fixed", TimeSpan.FromMinutes(210), "Iran", "Iran");
    }
}
