using ERP.Domain.Common;

namespace ERP.Domain.Catalog;

public readonly record struct UnitId
{
    private UnitId(Guid value)
    {
        Value = value;
    }

    public Guid Value { get; }

    public static UnitId New() => new(Guid.NewGuid());

    public static UnitId From(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new DomainException("شناسه واحد معتبر نیست.");
        }

        return new UnitId(value);
    }

    public override string ToString() => Value.ToString("D");
}

