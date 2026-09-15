using ERP.Domain.Common;

namespace ERP.Domain.Sales;

public readonly record struct SaleId
{
    private SaleId(Guid value)
    {
        Value = value;
    }

    public Guid Value { get; }

    public static SaleId New() => new(Guid.NewGuid());

    public static SaleId From(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new DomainException("شناسه فاکتور معتبر نیست.");
        }

        return new SaleId(value);
    }

    public override string ToString() => Value.ToString("D");
}
