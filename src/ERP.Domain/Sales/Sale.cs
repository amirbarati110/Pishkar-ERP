using ERP.Domain.Catalog;
using ERP.Domain.Common;
using ERP.Domain.Customers;
using ERP.Domain.Inventory;
using ERP.Domain.Sales.Events;

namespace ERP.Domain.Sales;

/// <summary>
/// A single POS transaction ("فروش سریع" / Quick Sale). Lifecycle: Draft (cart is
/// being built, freely editable) → Completed (paid, immutable, inventory
/// deducted) or Cancelled. Matches source-of-truth §2.9: invoice + stock must
/// commit together or not at all — this aggregate only computes and validates
/// totals; the Application handler is responsible for making the stock
/// deduction and the persisted Sale commit in the same transaction.
/// </summary>
public sealed class Sale : Entity<SaleId>
{
    private readonly List<SaleLine> _lines = [];

    private Sale(
        SaleId id,
        WarehouseId warehouseId,
        CustomerId? customerId,
        DateTimeOffset openedAtUtc)
        : base(id)
    {
        WarehouseId = warehouseId;
        CustomerId = customerId;
        OpenedAtUtc = openedAtUtc;
        Status = SaleStatus.Draft;
        Discount = Money.Zero;
        ServiceCharge = Money.Zero;
    }

    public WarehouseId WarehouseId { get; }

    /// <summary>Null is the walk-in cash sale («مشتری نقدی», §6.3).</summary>
    public CustomerId? CustomerId { get; private set; }

    public DateTimeOffset OpenedAtUtc { get; }

    public SaleStatus Status { get; private set; }

    /// <summary>
    /// Null while the sale is a draft. A number is drawn only when the invoice
    /// becomes a real financial document, so abandoned and held carts never burn
    /// one — see <see cref="SaleNumber"/> for why the sequence must stay
    /// ascending.
    /// </summary>
    public SaleNumber? Number { get; private set; }

    public Money Discount { get; private set; }

    public Money ServiceCharge { get; private set; }

    public PaymentMethod? PaymentMethod { get; private set; }

    public DateTimeOffset? CompletedAtUtc { get; private set; }

    /// <summary>
    /// The tax charged, fixed at completion. Stored rather than recomputed
    /// because the VAT rate is a setting that changes over time, and a posted
    /// invoice must keep the amount it was actually issued with. Null for a
    /// draft, and for sales completed before totals were recorded.
    /// </summary>
    public Money? Tax { get; private set; }

    /// <summary>The footer of a completed invoice; null while it is a draft.</summary>
    public SaleTotals? Totals => Tax is { } tax ? BuildTotals(tax) : null;

    public IReadOnlyList<SaleLine> Lines => _lines.AsReadOnly();

    public Money Subtotal => _lines.Aggregate(Money.Zero, (total, line) => total.Add(line.LineTotal));

    public static Sale OpenDraft(WarehouseId warehouseId, CustomerId? customerId, DateTimeOffset openedAtUtc)
    {
        return new Sale(SaleId.New(), warehouseId, customerId, openedAtUtc);
    }

    internal static Sale Rehydrate(
        SaleId id,
        WarehouseId warehouseId,
        CustomerId? customerId,
        DateTimeOffset openedAtUtc,
        SaleStatus status,
        SaleNumber? number,
        Money discount,
        Money serviceCharge,
        PaymentMethod? paymentMethod,
        DateTimeOffset? completedAtUtc,
        Money? tax,
        IEnumerable<(ProductId ProductId, decimal Quantity, long UnitPriceRials, long DiscountRials, long CatalogPriceRials)> lines)
    {
        var sale = new Sale(id, warehouseId, customerId, openedAtUtc)
        {
            Status = status,
            Number = number,
            Discount = discount,
            ServiceCharge = serviceCharge,
            PaymentMethod = paymentMethod,
            CompletedAtUtc = completedAtUtc,
            Tax = tax,
        };

        foreach (var line in lines)
        {
            var restored = new SaleLine(
                line.ProductId,
                Quantity.Create(line.Quantity),
                Money.FromRials(line.CatalogPriceRials == 0 ? line.UnitPriceRials : line.CatalogPriceRials));
            restored.Change(
                Quantity.Create(line.Quantity),
                Money.FromRials(line.UnitPriceRials),
                Money.FromRials(line.DiscountRials));
            sale._lines.Add(restored);
        }

        return sale;
    }

    /// <summary>
    /// Adds <paramref name="quantity"/> of a product to the cart at
    /// <paramref name="unitPrice"/> (a snapshot of the catalog price at the
    /// moment it was added — source-of-truth §6.11: the invoice price and the
    /// product master price are distinct once on an invoice). Scanning/adding a
    /// product already in the cart increases its quantity instead of adding a
    /// second row.
    /// </summary>
    public void AddOrIncreaseLine(ProductId productId, Quantity quantity, Money unitPrice)
    {
        EnsureDraft();

        var existing = _lines.Find(line => line.ProductId == productId);
        if (existing is not null)
        {
            existing.IncreaseQuantity(quantity);
            return;
        }

        _lines.Add(new SaleLine(productId, quantity, unitPrice));
    }

    public void RemoveLine(ProductId productId)
    {
        EnsureDraft();

        var existing = _lines.Find(line => line.ProductId == productId)
            ?? throw new DomainException("این کالا در سبد فاکتور نیست.");
        _lines.Remove(existing);
    }

    /// <summary>
    /// Edits one row: quantity, the price charged on this invoice, and the row
    /// discount (source-of-truth §6.10). Changing the price here never touches
    /// the product's master price — that is a separate, deliberate action on the
    /// catalogue (§6.11).
    /// </summary>
    public void ChangeLine(ProductId productId, Quantity quantity, Money unitPrice, Money discount)
    {
        EnsureDraft();

        var existing = _lines.Find(line => line.ProductId == productId)
            ?? throw new DomainException("این کالا در سبد فاکتور نیست.");
        existing.Change(quantity, unitPrice, discount);
    }

    /// <summary>
    /// Sets or changes who the invoice is for — «انتخاب مشتری» (F4) can happen
    /// at any point while the cart is being built, not only before the first
    /// scan. Null returns it to the walk-in cash sale. Whether the customer
    /// exists and may buy is checked by the Application handler, which can see
    /// the customer; this aggregate only guards its own lifecycle.
    /// </summary>
    public void AssignCustomer(CustomerId? customerId)
    {
        EnsureDraft();
        CustomerId = customerId;
    }

    public void ApplyDiscount(Money discount)
    {
        EnsureDraft();

        if (discount.Rials > Subtotal.Rials)
        {
            throw new DomainException("تخفیف نمی‌تواند از جمع کالاها بیشتر باشد.");
        }

        Discount = discount;
    }

    /// <summary>
    /// Extra charge on the invoice that is not a stocked product — delivery,
    /// packing, service fee ("خدمات / هزینه"، «ارسال» در نرم‌افزارهای رایج).
    /// It is added after the invoice discount and is taxed with the goods.
    /// </summary>
    public void ApplyServiceCharge(Money serviceCharge)
    {
        EnsureDraft();
        ServiceCharge = serviceCharge;
    }

    /// <summary>
    /// Finalizes the sale: stamps it with its invoice number, computes
    /// Subtotal/Discount/Tax/Total, locks the sale against further edits, and
    /// raises <see cref="SaleCompleted"/>. Does <b>not</b> touch inventory — the
    /// Application handler owns pulling stock via
    /// <see cref="StockLedger.ConsumeFifo"/> for each line and persisting both
    /// changes in one transaction.
    /// </summary>
    /// <param name="number">
    /// Drawn by the Application handler from the database sequence, for the same
    /// reason the tax rate is passed in: allocating a shared, monotonic serial is
    /// infrastructure, not a rule this aggregate can enforce on its own.
    /// </param>
    /// <param name="taxRatePercent">
    /// Passed in rather than a domain constant on purpose: Iran's VAT rate is a
    /// configuration/tax-infrastructure concern (source-of-truth Phase 4,
    /// chapter 10.10), not a Sales domain rule. The caller supplies it.
    /// </param>
    public SaleTotals Complete(
        SaleNumber number,
        Sales.PaymentMethod paymentMethod,
        decimal taxRatePercent,
        DateTimeOffset completedAtUtc)
    {
        EnsureDraft();

        if (_lines.Count == 0)
        {
            throw new DomainException("فاکتور خالی است؛ حداقل یک کالا اضافه کنید.");
        }

        // نسیه و چک یعنی کسی باید پول را بعداً بدهد؛ بدون مشتری معلوم نیست چه کسی.
        if (paymentMethod is Sales.PaymentMethod.Credit or Sales.PaymentMethod.Cheque && CustomerId is null)
        {
            throw new DomainException("برای فروش نسیه یا چکی، اول مشتری را انتخاب کنید.");
        }

        var totals = PreviewTotals(taxRatePercent);

        Status = SaleStatus.Completed;
        Number = number;
        PaymentMethod = paymentMethod;
        CompletedAtUtc = completedAtUtc;
        Tax = totals.Tax;

        Raise(new SaleCompleted(Id, WarehouseId, CustomerId, totals.Total, completedAtUtc));

        return totals;
    }

    /// <summary>
    /// What the footer would show if the sale were completed now at
    /// <paramref name="taxRatePercent"/>. Used before completion by checks
    /// that need the payable amount — the credit limit (§6.7) — and by
    /// <see cref="Complete"/> itself, so both always agree to the Rial.
    /// </summary>
    public SaleTotals PreviewTotals(decimal taxRatePercent)
    {
        if (taxRatePercent < 0)
        {
            throw new DomainException("درصد مالیات نمی‌تواند منفی باشد.");
        }

        // جمع کالاها − تخفیف + خدمات/هزینه → مالیات روی همین مبلغ → قابل پرداخت
        var taxableAmount = Subtotal.Subtract(Discount).Add(ServiceCharge);
        return BuildTotals(taxableAmount.Multiply(taxRatePercent / 100m));
    }

    private SaleTotals BuildTotals(Money tax)
    {
        var total = Subtotal.Subtract(Discount).Add(ServiceCharge).Add(tax);
        return new SaleTotals(Subtotal, Discount, ServiceCharge, tax, total);
    }

    public void Cancel()
    {
        EnsureDraft();
        Status = SaleStatus.Cancelled;
    }

    private void EnsureDraft()
    {
        if (Status != SaleStatus.Draft)
        {
            throw new DomainException("این فاکتور دیگر در حالت پیش‌نویس نیست و قابل ویرایش نیست.");
        }
    }
}
