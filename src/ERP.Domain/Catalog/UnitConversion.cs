using ERP.Domain.Common;

namespace ERP.Domain.Catalog;

public sealed class UnitConversion
{
    private UnitConversion(UnitId fromUnitId, UnitId toUnitId, decimal factor)
    {
        FromUnitId = fromUnitId;
        ToUnitId = toUnitId;
        Factor = factor;
    }

    public UnitId FromUnitId { get; }

    public UnitId ToUnitId { get; }

    public decimal Factor { get; }

    public static UnitConversion Create(UnitId fromUnitId, UnitId toUnitId, decimal factor)
    {
        if (fromUnitId == toUnitId)
        {
            throw new DomainException("واحد مبدأ و مقصد باید متفاوت باشند.");
        }

        if (factor <= 0)
        {
            throw new DomainException("ضریب تبدیل باید بیشتر از صفر باشد.");
        }

        return new UnitConversion(fromUnitId, toUnitId, factor);
    }

    public Quantity Convert(Quantity quantity) => Quantity.Create(checked(quantity.Value * Factor));
}

