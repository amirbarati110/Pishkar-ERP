using ERP.Domain.Common;

namespace ERP.Domain.Catalog;

public readonly record struct ProductBarcode
{
    private const int MaximumLength = 64;

    private ProductBarcode(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static ProductBarcode Create(string barcode)
    {
        if (string.IsNullOrWhiteSpace(barcode))
        {
            throw new DomainException("بارکد را وارد کنید.");
        }

        var normalized = barcode.Trim().ToUpperInvariant();

        if (normalized.Any(char.IsWhiteSpace))
        {
            throw new DomainException("بارکد نمی‌تواند فاصله داشته باشد.");
        }

        if (normalized.Length > MaximumLength)
        {
            throw new DomainException("بارکد نمی‌تواند بیشتر از ۶۴ نویسه باشد.");
        }

        return new ProductBarcode(normalized);
    }

    public override string ToString() => Value;
}

