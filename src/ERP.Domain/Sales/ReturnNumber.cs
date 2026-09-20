using ERP.Domain.Common;

namespace ERP.Domain.Sales;

/// <summary>
/// The number a return document is read out by. Same guarantees as
/// <see cref="SaleNumber"/> (ascending, no year, gaps allowed) but its own
/// sequence: a «برگشت از فروش» is a separate document, not an invoice, and
/// mixing the two counters would leave holes the tax authority reads as
/// missing invoices.
/// </summary>
public readonly record struct ReturnNumber
{
    private ReturnNumber(long value)
    {
        Value = value;
    }

    public long Value { get; }

    public static ReturnNumber From(long value)
    {
        if (value <= 0)
        {
            throw new DomainException("شماره مرجوعی باید بزرگ‌تر از صفر باشد.");
        }

        return new ReturnNumber(value);
    }

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public string ToPersianString() => PersianNumber.Format(Value);
}
