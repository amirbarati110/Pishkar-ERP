using ERP.Domain.Catalog;
using ERP.Domain.Common;

namespace ERP.Domain.Tests.Catalog;

public sealed class UnitConversionTests
{
    [Fact]
    public void CreateUnitRequiresPersianName()
    {
        var exception = Assert.Throws<DomainException>(() => Unit.Create(" ", "kg", true));

        Assert.Equal("نام واحد را وارد کنید.", exception.Message);
    }

    [Fact]
    public void CreateConversionRejectsSameSourceAndDestination()
    {
        var unitId = UnitId.New();

        var exception = Assert.Throws<DomainException>(() => UnitConversion.Create(unitId, unitId, 12m));

        Assert.Equal("واحد مبدأ و مقصد باید متفاوت باشند.", exception.Message);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-12")]
    public void CreateConversionRejectsNonPositiveFactor(string rawFactor)
    {
        var factor = decimal.Parse(rawFactor, System.Globalization.CultureInfo.InvariantCulture);

        var exception = Assert.Throws<DomainException>(() =>
            UnitConversion.Create(UnitId.New(), UnitId.New(), factor));

        Assert.Equal("ضریب تبدیل باید بیشتر از صفر باشد.", exception.Message);
    }

    [Fact]
    public void ConvertMultipliesByConfiguredFactor()
    {
        var conversion = UnitConversion.Create(UnitId.New(), UnitId.New(), 12m);

        var converted = conversion.Convert(Quantity.Create(2m));

        Assert.Equal(24m, converted.Value);
    }
}
