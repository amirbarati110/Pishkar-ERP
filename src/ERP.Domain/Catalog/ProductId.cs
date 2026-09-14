using ERP.Domain.Common;

namespace ERP.Domain.Catalog;

public readonly record struct ProductId
{
    private ProductId(Guid value)
    {
        Value = value;
    }

    public Guid Value { get; }

    public static ProductId New() => new(Guid.NewGuid());

    public static ProductId From(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new DomainException("شناسه کالا معتبر نیست.");
        }

        return new ProductId(value);
    }

    public override string ToString() => Value.ToString("D");
}

