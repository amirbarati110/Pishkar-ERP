using ERP.Domain.Common;

namespace ERP.Domain.Identity;

public readonly record struct UserId
{
    private UserId(Guid value)
    {
        Value = value;
    }

    public Guid Value { get; }

    public static UserId New() => new(Guid.NewGuid());

    public static UserId From(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new DomainException("شناسه کاربر معتبر نیست.");
        }

        return new UserId(value);
    }

    public override string ToString() => Value.ToString("D");
}
