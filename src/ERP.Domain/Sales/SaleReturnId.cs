using ERP.Domain.Common;

namespace ERP.Domain.Sales;

public readonly record struct SaleReturnId
{
    private SaleReturnId(Guid value)
    {
        Value = value;
    }

    public Guid Value { get; }

    public static SaleReturnId New() => new(Guid.NewGuid());

    public static SaleReturnId From(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new DomainException("شناسه مرجوعی معتبر نیست.");
        }

        return new SaleReturnId(value);
    }

    public override string ToString() => Value.ToString("D");
}
