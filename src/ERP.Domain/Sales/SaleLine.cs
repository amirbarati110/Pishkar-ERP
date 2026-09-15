using ERP.Domain.Catalog;
using ERP.Domain.Common;

namespace ERP.Domain.Sales;

/// <summary>
/// One product row in a <see cref="Sale"/>'s cart. Kept by <see cref="ProductId"/>
/// as the natural key rather than its own identity: scanning/adding the same
/// product twice increases the existing line's quantity (matching the +/- stepper
/// behaviour in the cashier screen, not a duplicate row per scan).
/// </summary>
public sealed class SaleLine
{
    internal SaleLine(ProductId productId, Quantity quantity, Money unitPrice)
    {
        ProductId = productId;
        Quantity = quantity;
        UnitPrice = unitPrice;
        CatalogPrice = unitPrice;
        Discount = Money.Zero;
    }

    public ProductId ProductId { get; }

    public Quantity Quantity { get; private set; }

    /// <summary>The price actually charged on this invoice.</summary>
    public Money UnitPrice { get; private set; }

    /// <summary>
    /// The product's master price at the moment it was added. Kept beside
    /// <see cref="UnitPrice"/> so the screen can flag "قیمت این فاکتور تغییر کرده"
    /// and so a later report can tell a one-off price from the catalogue price
    /// (source-of-truth §6.11).
    /// </summary>
    public Money CatalogPrice { get; }

    /// <summary>Row-level discount — §6.10 allows both an amount and a percentage;
    /// the percentage is converted to an amount by the caller, since money is what
    /// the ledger stores.</summary>
    public Money Discount { get; private set; }

    public bool PriceOverridden => UnitPrice.Rials != CatalogPrice.Rials;

    public Money GrossAmount => UnitPrice.Multiply(Quantity.Value);

    public Money LineTotal => GrossAmount.Subtract(Discount);

    internal void IncreaseQuantity(Quantity by) => Quantity = Quantity.Add(by);

    internal void Change(Quantity quantity, Money unitPrice, Money discount)
    {
        var gross = unitPrice.Multiply(quantity.Value);

        if (discount.Rials > gross.Rials)
        {
            throw new DomainException("تخفیف سطر نمی‌تواند از مبلغ همان سطر بیشتر باشد.");
        }

        Quantity = quantity;
        UnitPrice = unitPrice;
        Discount = discount;
    }
}
