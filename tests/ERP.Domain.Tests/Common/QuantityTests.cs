using ERP.Domain.Common;

namespace ERP.Domain.Tests.Common;

public sealed class QuantityTests
{
    [Theory]
    [InlineData("0")]
    [InlineData("-0.001")]
    public void CreateRejectsNonPositiveValue(string rawValue)
    {
        var value = decimal.Parse(rawValue, System.Globalization.CultureInfo.InvariantCulture);

        var exception = Assert.Throws<DomainException>(() => Quantity.Create(value));

        Assert.Equal("مقدار باید بیشتر از صفر باشد.", exception.Message);
    }

    [Fact]
    public void AddReturnsCombinedQuantity()
    {
        var first = Quantity.Create(2.5m);
        var second = Quantity.Create(1.25m);

        var total = first.Add(second);

        Assert.Equal(3.75m, total.Value);
    }

    [Fact]
    public void SubtractRejectsNonPositiveResult()
    {
        var quantity = Quantity.Create(2m);

        var exception = Assert.Throws<DomainException>(() => quantity.Subtract(Quantity.Create(2m)));

        Assert.Equal("مقدار باقی‌مانده باید بیشتر از صفر باشد.", exception.Message);
    }
}
