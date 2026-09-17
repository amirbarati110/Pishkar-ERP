using ERP.Domain.Common;

namespace ERP.Domain.Cashiering;

public readonly record struct CashShiftId
{
    private CashShiftId(Guid value)
    {
        Value = value;
    }

    public Guid Value { get; }

    public static CashShiftId New() => new(Guid.NewGuid());

    public static CashShiftId From(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new DomainException("شناسه شیفت صندوق معتبر نیست.");
        }

        return new CashShiftId(value);
    }

    public override string ToString() => Value.ToString("D");
}
