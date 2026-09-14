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
}
