using ERP.Domain.Common;

namespace ERP.Domain.Customers;

public readonly record struct CustomerId
{
    private CustomerId(Guid value)
    {
        Value = value;
    }

    public Guid Value { get; }

    public static CustomerId New() => new(Guid.NewGuid());

    public static CustomerId From(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new DomainException("شناسه مشتری معتبر نیست.");
        }

        return new CustomerId(value);
    }

    public override string ToString() => Value.ToString("D");
}
