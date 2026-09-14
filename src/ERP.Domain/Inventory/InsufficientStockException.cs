using ERP.Domain.Common;

namespace ERP.Domain.Inventory;

public sealed class InsufficientStockException : DomainException
{
    public InsufficientStockException(decimal availableQuantity)
        : base($"موجودی کالا کافی نیست. موجودی فعلی: {PersianNumber.Format(availableQuantity)}")
    {
        AvailableQuantity = availableQuantity;
    }

    public decimal AvailableQuantity { get; }
}

