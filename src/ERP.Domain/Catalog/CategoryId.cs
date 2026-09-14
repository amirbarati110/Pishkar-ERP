using ERP.Domain.Common;

namespace ERP.Domain.Catalog;

public readonly record struct CategoryId
{
    private CategoryId(Guid value)
    {
        Value = value;
    }

    public Guid Value { get; }

    public static CategoryId New() => new(Guid.NewGuid());

    public static CategoryId From(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new DomainException("شناسه دسته‌بندی معتبر نیست.");
        }

        return new CategoryId(value);
    }

    public override string ToString() => Value.ToString("D");
}

