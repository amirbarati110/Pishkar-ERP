using ERP.Domain.Common;

namespace ERP.Domain.Tests.Common;

public sealed class MoneyTests
{
    [Fact]
    public void FromTomansConvertsToRialsWithoutRounding()
    {
        var money = Money.FromTomans(12_500);

        Assert.Equal(125_000, money.Rials);
    }

    [Fact]
    public void FromRialsRetainsExactValue()
    {
        var money = Money.FromRials(125_001);

        Assert.Equal(125_001, money.Rials);
    }

    [Fact]
    public void AddThrowsOnOverflow()
    {
        var maximum = Money.FromRials(long.MaxValue);

        Assert.Throws<OverflowException>(() => maximum.Add(Money.FromRials(1)));
    }

    [Fact]
    public void FromRialsRejectsNegativeAmount()
    {
        var exception = Assert.Throws<DomainException>(() => Money.FromRials(-1));

        Assert.Equal("مبلغ نمی‌تواند منفی باشد.", exception.Message);
    }

    [Fact]
    public void ToTomansExactRejectsAmountThatIsNotWholeToman()
    {
        var money = Money.FromRials(101);

        var exception = Assert.Throws<DomainException>(() => money.ToTomansExact());

        Assert.Equal("این مبلغ، معادل دقیق تومان ندارد.", exception.Message);
    }

    [Fact]
    public void MultiplyScalesByAWholeFactor()
    {
        var unitPrice = Money.FromTomans(1_250_000);

        var lineTotal = unitPrice.Multiply(3);

        Assert.Equal(37_500_000, lineTotal.Rials);
    }

    [Fact]
    public void MultiplyRoundsAFractionalFactorToTheNearestRial()
    {
        // A sale line for 1.5 kg at 10,000 Tomans/kg = 150,000 Rials exactly,
        // but 1.333 kg at the same price must round rather than truncate/throw.
        var unitPrice = Money.FromTomans(10_000);

        var lineTotal = unitPrice.Multiply(1.333m);

        Assert.Equal(133_300, lineTotal.Rials);
    }

    [Fact]
    public void MultiplyRejectsNegativeFactor()
    {
        var unitPrice = Money.FromTomans(10_000);

        var exception = Assert.Throws<DomainException>(() => unitPrice.Multiply(-1));

        Assert.Equal("ضریب نمی‌تواند منفی باشد.", exception.Message);
    }
}
