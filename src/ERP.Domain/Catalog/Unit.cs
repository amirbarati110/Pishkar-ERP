using ERP.Domain.Common;

namespace ERP.Domain.Catalog;

public sealed class Unit : Entity<UnitId>
{
    private const int MaximumNameLength = 60;
    private const int MaximumSymbolLength = 12;

    private Unit(UnitId id, string name, string symbol, bool allowsFractions)
        : base(id)
    {
        Name = name;
        Symbol = symbol;
        AllowsFractions = allowsFractions;
    }

    public string Name { get; private set; }

    public string Symbol { get; private set; }

    public bool AllowsFractions { get; private set; }

    public static Unit Create(string name, string symbol, bool allowsFractions)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("نام واحد را وارد کنید.");
        }

        if (string.IsNullOrWhiteSpace(symbol))
        {
            throw new DomainException("نماد واحد را وارد کنید.");
        }

        var normalizedName = name.Trim();
        var normalizedSymbol = symbol.Trim();

        if (normalizedName.Length > MaximumNameLength)
        {
            throw new DomainException("نام واحد نمی‌تواند بیشتر از ۶۰ نویسه باشد.");
        }

        if (normalizedSymbol.Length > MaximumSymbolLength)
        {
            throw new DomainException("نماد واحد نمی‌تواند بیشتر از ۱۲ نویسه باشد.");
        }

        return new Unit(UnitId.New(), normalizedName, normalizedSymbol, allowsFractions);
    }
}

