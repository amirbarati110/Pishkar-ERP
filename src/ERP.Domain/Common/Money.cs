namespace ERP.Domain.Common;

public readonly record struct Money
{
    private const long RialsPerToman = 10;

    private Money(long rials)
    {
        Rials = rials;
    }

    public long Rials { get; }

    public static Money Zero => new(0);

    public static Money FromRials(long rials)
    {
        if (rials < 0)
        {
            throw new DomainException("مبلغ نمی‌تواند منفی باشد.");
        }

        return new Money(rials);
    }

    public static Money FromTomans(long tomans)
    {
        if (tomans < 0)
        {
            throw new DomainException("مبلغ نمی‌تواند منفی باشد.");
        }

        return new Money(checked(tomans * RialsPerToman));
    }

    public Money Add(Money other) => new(checked(Rials + other.Rials));

    public Money Subtract(Money other)
    {
        var result = checked(Rials - other.Rials);

        if (result < 0)
        {
            throw new DomainException("مبلغ باقی‌مانده نمی‌تواند منفی باشد.");
        }

        return new Money(result);
    }

    public long ToTomansExact()
    {
        if (Rials % RialsPerToman != 0)
        {
            throw new DomainException("این مبلغ، معادل دقیق تومان ندارد.");
        }

        return Rials / RialsPerToman;
    }
}

