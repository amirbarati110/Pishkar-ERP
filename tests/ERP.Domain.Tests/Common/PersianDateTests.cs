using ERP.Domain.Common;

namespace ERP.Domain.Tests.Common;

public sealed class PersianDateTests
{
    [Fact]
    public void ConvertsAGregorianMomentToTheIranianDate()
    {
        // ۲۳ شهریور ۱۴۰۵ = 14 September 2026 (the date on the reference screens)
        var date = PersianDate.FromUtc(new DateTimeOffset(2026, 9, 14, 8, 0, 0, TimeSpan.Zero));

        Assert.Equal(PersianDate.Create(1405, 6, 23), date);
        Assert.Equal("۱۴۰۵/۰۶/۲۳", date.ToString());
    }

    [Fact]
    public void JustAfterMidnightInTehranIsAlreadyTheNextDayEvenThoughUtcIsNot()
    {
        // 00:30 Tehran on ۲۴ شهریور = 21:00 UTC on 14 September
        var justAfterMidnight = new DateTimeOffset(2026, 9, 14, 21, 0, 0, TimeSpan.Zero);

        Assert.Equal(PersianDate.Create(1405, 6, 24), PersianDate.FromUtc(justAfterMidnight));
        Assert.Equal(new TimeOnly(0, 30), PersianDate.TimeOfDayInIran(justAfterMidnight));
    }

    [Fact]
    public void ADayRunsFromTehranMidnightToTehranMidnightInUtc()
    {
        var day = PersianDate.Create(1405, 6, 23);

        Assert.Equal(new DateTimeOffset(2026, 9, 13, 20, 30, 0, TimeSpan.Zero), day.StartUtc);
        Assert.Equal(new DateTimeOffset(2026, 9, 14, 20, 30, 0, TimeSpan.Zero), day.EndUtc);
    }

    [Fact]
    public void AddingDaysCrossesMonthAndYearBoundaries()
    {
        Assert.Equal(PersianDate.Create(1405, 7, 1), PersianDate.Create(1405, 6, 31).AddDays(1));
        Assert.Equal(PersianDate.Create(1406, 1, 1), PersianDate.Create(1405, 12, 29).AddDays(1));
    }

    [Theory]
    [InlineData(1405, 13, 1)]
    [InlineData(1405, 7, 31)]  // مهر ۳۰ روز است
    [InlineData(1405, 0, 1)]
    public void RejectsADayThatDoesNotExist(int year, int month, int day)
    {
        var exception = Assert.Throws<DomainException>(() => PersianDate.Create(year, month, day));

        Assert.Equal("تاریخ شمسی معتبر نیست.", exception.Message);
    }

    [Fact]
    public void GroupsMoneyWithPersianDigits()
    {
        Assert.Equal("۱۲,۵۰۰,۰۰۰", PersianNumber.FormatGrouped(12_500_000));
        Assert.Equal("۰", PersianNumber.FormatGrouped(0));
    }
}
