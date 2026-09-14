namespace ERP.Domain.Common;

public readonly record struct Quantity
{
    private Quantity(decimal value)
    {
        Value = value;
    }

    public decimal Value { get; }

    public static Quantity Create(decimal value)
    {
        if (value <= 0)
        {
            throw new DomainException("مقدار باید بیشتر از صفر باشد.");
        }

        return new Quantity(value);
    }

    public Quantity Add(Quantity other) => new(checked(Value + other.Value));

    public Quantity Subtract(Quantity other)
    {
        var result = checked(Value - other.Value);

        if (result <= 0)
        {
            throw new DomainException("مقدار باقی‌مانده باید بیشتر از صفر باشد.");
        }

        return new Quantity(result);
    }
}
