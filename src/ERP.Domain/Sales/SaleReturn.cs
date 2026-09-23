using ERP.Domain.Catalog;
using ERP.Domain.Common;
using ERP.Domain.Customers;
using ERP.Domain.Inventory;

namespace ERP.Domain.Sales;

/// <summary>One item the cashier wants to take back: how much, and where the goods go.</summary>
public sealed record ReturnLineRequest(ProductId ProductId, decimal Quantity, ReturnDisposition Disposition);

/// <summary>What earlier returns of the same invoice line already gave back — needed so the last piece can take the rounding remainder.</summary>
public sealed record PreviouslyReturned(decimal Quantity, Money Net, Money Tax);

/// <summary>
/// The invoice's «خدمات/هزینه» (delivery, packing, service fee) and the VAT charged on it, as a
/// return would give them back. Zero when the invoice had no service charge.
/// </summary>
public sealed record ServiceChargeRefund(Money Net, Money Tax)
{
    public static ServiceChargeRefund None { get; } = new(Money.Zero, Money.Zero);

    public Money Total => Net.Add(Tax);

    public bool IsEmpty => Net.Rials == 0 && Tax.Rials == 0;
}

/// <summary>
/// One returned row, already priced. <see cref="Net"/> is the goods' value
/// after the row and invoice discounts, <see cref="Tax"/> the VAT that was
/// charged on exactly that value — both fixed here, never recomputed, because
/// a return is a posted document (Codex rule 15).
/// </summary>
public sealed class SaleReturnLine
{
    internal SaleReturnLine(
        ProductId productId,
        Quantity quantity,
        ReturnDisposition disposition,
        Money net,
        Money tax,
        Money? unitCost)
    {
        ProductId = productId;
        Quantity = quantity;
        Disposition = disposition;
        Net = net;
        Tax = tax;
        UnitCost = unitCost;
    }

    public ProductId ProductId { get; }

    public Quantity Quantity { get; }

    public ReturnDisposition Disposition { get; }

    public Money Net { get; }

    public Money Tax { get; }

    /// <summary>What the goods originally cost per unit — null when the sale predates cost recording and the row is not going back to stock.</summary>
    public Money? UnitCost { get; }

    public Money Refund => Net.Add(Tax);

    /// <summary>Cost of the goods that go back on the shelf; zero for damaged/waste rows and rows with no known cost.</summary>
    public Money RestockCost => Disposition == ReturnDisposition.ToStock && UnitCost is { } cost
        ? cost.Multiply(Quantity.Value)
        : Money.Zero;
}

/// <summary>
/// «مرجوعی» (§6.25): giving back part or all of a completed invoice. Like the
/// invoice it comes from, it is immutable once made; a mistaken return is
/// answered by a new document, never by an edit.
/// </summary>
public sealed class SaleReturn : Entity<SaleReturnId>
{
    private const int MaximumReasonLength = 300;

    private readonly List<SaleReturnLine> _lines = [];

    private SaleReturn(
        SaleReturnId id,
        SaleId saleId,
        WarehouseId warehouseId,
        CustomerId? customerId,
        ReturnNumber number,
        PaymentMethod refundMethod,
        string reason,
        DateTimeOffset completedAtUtc)
        : base(id)
    {
        SaleId = saleId;
        WarehouseId = warehouseId;
        CustomerId = customerId;
        Number = number;
        RefundMethod = refundMethod;
        Reason = reason;
        CompletedAtUtc = completedAtUtc;
    }

    /// <summary>The invoice this return is against — the one the customer holds, i.e. the correction when the original was corrected.</summary>
    public SaleId SaleId { get; }

    public WarehouseId WarehouseId { get; }

    public CustomerId? CustomerId { get; }

    public ReturnNumber Number { get; }

    /// <summary>Cash, Card, or Credit (reduces what the customer owes). Cheque is not a way to refund.</summary>
    public PaymentMethod RefundMethod { get; }

    public string Reason { get; }

    public DateTimeOffset CompletedAtUtc { get; }

    public IReadOnlyList<SaleReturnLine> Lines => _lines.AsReadOnly();

    /// <summary>
    /// The invoice's service charge and its VAT, when the cashier chose to give it back with this
    /// return (user decision 1405/07/02: optional, not automatic — a delivery that happened is
    /// normally kept, one that did not is refunded). <see cref="ServiceChargeRefund.None"/> otherwise.
    /// Given back at most once per invoice.
    /// </summary>
    public ServiceChargeRefund ServiceCharge { get; private init; } = ServiceChargeRefund.None;

    /// <summary>Goods and, when refunded, the service charge — before VAT.</summary>
    public Money TotalNet => _lines.Aggregate(ServiceCharge.Net, (total, line) => total.Add(line.Net));

    public Money TotalTax => _lines.Aggregate(ServiceCharge.Tax, (total, line) => total.Add(line.Tax));

    /// <summary>What goes back to the customer.</summary>
    public Money RefundTotal => TotalNet.Add(TotalTax);

    public Money RestockCost => _lines.Aggregate(Money.Zero, (total, line) => total.Add(line.RestockCost));

    /// <summary>
    /// Prices what would be refunded without creating anything — the screen
    /// shows this while the cashier picks quantities, and
    /// <see cref="Create"/> uses the very same code, so the number on screen
    /// is the number that gets refunded.
    /// </summary>
    /// <param name="includesServiceCharge">
    /// True when the service charge is being given back too — then a return with no goods at all
    /// (only the service charge, e.g. a delivery that never happened) is allowed.
    /// </param>
    public static IReadOnlyList<SaleReturnLine> Price(
        Sale sale,
        IReadOnlyCollection<ReturnLineRequest> requests,
        IReadOnlyDictionary<ProductId, PreviouslyReturned> previouslyReturned,
        IReadOnlyDictionary<ProductId, Money> unitCosts,
        bool includesServiceCharge = false)
    {
        ArgumentNullException.ThrowIfNull(sale);
        ArgumentNullException.ThrowIfNull(requests);

        EnsureReturnable(sale);

        if (requests.Count == 0 && !includesServiceCharge)
        {
            throw new DomainException("هیچ کالایی برای مرجوعی انتخاب نشده است.");
        }

        if (requests.Select(request => request.ProductId).Distinct().Count() != requests.Count)
        {
            throw new DomainException("یک کالا نمی‌تواند دو بار در یک مرجوعی بیاید.");
        }

        var (discountFactor, taxFactor) = Factors(sale);

        var priced = new List<SaleReturnLine>(requests.Count);
        foreach (var request in requests)
        {
            var saleLine = sale.Lines.FirstOrDefault(line => line.ProductId == request.ProductId)
                ?? throw new DomainException("این کالا در فاکتور اصلی نیست.");

            if (request.Quantity <= 0)
            {
                throw new DomainException("تعداد مرجوعی باید بیشتر از صفر باشد.");
            }

            var previous = previouslyReturned.GetValueOrDefault(request.ProductId)
                ?? new PreviouslyReturned(0, Money.Zero, Money.Zero);
            var remaining = saleLine.Quantity.Value - previous.Quantity;
            if (request.Quantity > remaining)
            {
                throw new DomainException(
                    remaining <= 0
                        ? "همه‌ی این کالا قبلاً مرجوع شده است."
                        : $"از این کالا فقط {PersianNumber.Format(remaining)} عدد قابل مرجوعی است.");
            }

            var (taxableFull, taxFull) = FullLineValue(saleLine, discountFactor, taxFactor);

            Money net;
            Money lineTax;
            if (request.Quantity == remaining)
            {
                // The last piece takes what is left, so partial returns that
                // add up to the whole line refund exactly the invoice's value.
                net = Money.FromRials(Math.Max(0, taxableFull.Rials - previous.Net.Rials));
                lineTax = Money.FromRials(Math.Max(0, taxFull.Rials - previous.Tax.Rials));
            }
            else
            {
                var share = request.Quantity / saleLine.Quantity.Value;
                net = taxableFull.Multiply(share);
                lineTax = taxFull.Multiply(share);
            }

            var cost = unitCosts.TryGetValue(request.ProductId, out var known) ? known : (Money?)null;
            if (request.Disposition == ReturnDisposition.ToStock && cost is null)
            {
                throw new DomainException("بهای تمام‌شده‌ی این کالا در فاکتور ثبت نشده؛ فقط به‌عنوان «خراب» یا «ضایعات» می‌شود برگرداند.");
            }

            priced.Add(new SaleReturnLine(
                request.ProductId,
                Quantity.Create(request.Quantity),
                request.Disposition,
                net,
                lineTax,
                cost));
        }

        return priced;
    }

    /// <summary>
    /// What giving back the invoice's service charge refunds: the charge itself and the VAT on it.
    /// The VAT is the invoice's VAT minus what every goods line carries when fully returned, so
    /// returning everything — goods and service charge — gives back the invoice's VAT to the Rial.
    /// </summary>
    /// <param name="alreadyRefunded">An earlier return of this invoice already gave it back.</param>
    public static ServiceChargeRefund PriceServiceCharge(Sale sale, bool alreadyRefunded)
    {
        ArgumentNullException.ThrowIfNull(sale);
        EnsureReturnable(sale);

        if (sale.ServiceCharge.Rials == 0)
        {
            throw new DomainException("این فاکتور «خدمات/هزینه» ندارد که پس داده شود.");
        }

        if (alreadyRefunded)
        {
            throw new DomainException("«خدمات/هزینه»ی این فاکتور قبلاً در یک مرجوعی دیگر پس داده شده است.");
        }

        var (discountFactor, taxFactor) = Factors(sale);
        var goodsTax = sale.Lines.Sum(line => FullLineValue(line, discountFactor, taxFactor).Tax.Rials);
        var serviceTax = Math.Max(0, (sale.Tax?.Rials ?? 0) - goodsTax);
        return new ServiceChargeRefund(sale.ServiceCharge, Money.FromRials(serviceTax));
    }

    /// <summary>
    /// What the return window may offer for the service charge: <see cref="ServiceChargeRefund.None"/>
    /// when the invoice has none or it was already given back, never an exception.
    /// </summary>
    public static ServiceChargeRefund RefundableServiceCharge(Sale sale, bool alreadyRefunded)
    {
        ArgumentNullException.ThrowIfNull(sale);
        return sale.ServiceCharge.Rials == 0 || alreadyRefunded || sale.Status != SaleStatus.Completed
            ? ServiceChargeRefund.None
            : PriceServiceCharge(sale, alreadyRefunded);
    }

    private static void EnsureReturnable(Sale sale)
    {
        if (sale.Status != SaleStatus.Completed || sale.Number is null)
        {
            throw new DomainException("فقط از فاکتور ثبت‌شده می‌شود مرجوعی گرفت.");
        }
    }

    /// <summary>The invoice's discount as a share of the goods, and its VAT as a share of what was taxed.</summary>
    private static (decimal Discount, decimal Tax) Factors(Sale sale)
    {
        var subtotal = sale.Subtotal.Rials;
        var discountFactor = subtotal > 0 ? (decimal)sale.Discount.Rials / subtotal : 0m;
        var taxBase = subtotal - sale.Discount.Rials + sale.ServiceCharge.Rials;
        var taxFactor = taxBase > 0 && sale.Tax is { } tax ? (decimal)tax.Rials / taxBase : 0m;
        return (discountFactor, taxFactor);
    }

    /// <summary>A whole invoice line's value after its share of the invoice discount, and the VAT on it.</summary>
    private static (Money Net, Money Tax) FullLineValue(SaleLine saleLine, decimal discountFactor, decimal taxFactor)
    {
        var lineNet = saleLine.LineTotal;
        var taxableFull = Money.FromRials(lineNet.Rials - lineNet.Multiply(discountFactor).Rials);
        return (taxableFull, taxableFull.Multiply(taxFactor));
    }

    /// <param name="refundServiceCharge">The cashier ticked «خدمات/هزینه هم پس داده شود».</param>
    /// <param name="serviceChargeAlreadyRefunded">An earlier return of this invoice already gave the service charge back.</param>
    public static SaleReturn Create(
        Sale sale,
        ReturnNumber number,
        IReadOnlyCollection<ReturnLineRequest> requests,
        PaymentMethod refundMethod,
        string? reason,
        IReadOnlyDictionary<ProductId, PreviouslyReturned> previouslyReturned,
        IReadOnlyDictionary<ProductId, Money> unitCosts,
        DateTimeOffset completedAtUtc,
        bool refundServiceCharge = false,
        bool serviceChargeAlreadyRefunded = false)
    {
        ArgumentNullException.ThrowIfNull(sale);

        if (refundMethod == PaymentMethod.Cheque)
        {
            throw new DomainException("پول مرجوعی را نمی‌شود با چک برگرداند؛ نقدی، کارتخوان یا کم‌کردن از بدهی را انتخاب کنید.");
        }

        if (refundMethod == PaymentMethod.Credit && sale.CustomerId is null)
        {
            throw new DomainException("برای کم‌کردن از بدهی، فاکتور باید مشتری داشته باشد.");
        }

        var trimmedReason = reason?.Trim();
        if (string.IsNullOrEmpty(trimmedReason))
        {
            throw new DomainException("دلیل مرجوعی را بنویسید.");
        }

        if (trimmedReason.Length > MaximumReasonLength)
        {
            throw new DomainException("دلیل مرجوعی نمی‌تواند بیشتر از ۳۰۰ نویسه باشد.");
        }

        var lines = Price(sale, requests, previouslyReturned, unitCosts, refundServiceCharge);
        var serviceCharge = refundServiceCharge
            ? PriceServiceCharge(sale, serviceChargeAlreadyRefunded)
            : ServiceChargeRefund.None;

        var saleReturn = new SaleReturn(
            SaleReturnId.New(),
            sale.Id,
            sale.WarehouseId,
            sale.CustomerId,
            number,
            refundMethod,
            trimmedReason,
            completedAtUtc)
        {
            ServiceCharge = serviceCharge,
        };
        saleReturn._lines.AddRange(lines);
        return saleReturn;
    }

    internal static SaleReturn Rehydrate(
        SaleReturnId id,
        SaleId saleId,
        WarehouseId warehouseId,
        CustomerId? customerId,
        ReturnNumber number,
        PaymentMethod refundMethod,
        string reason,
        DateTimeOffset completedAtUtc,
        IEnumerable<SaleReturnLine> lines,
        ServiceChargeRefund? serviceCharge = null)
    {
        var saleReturn = new SaleReturn(id, saleId, warehouseId, customerId, number, refundMethod, reason, completedAtUtc)
        {
            ServiceCharge = serviceCharge ?? ServiceChargeRefund.None,
        };
        saleReturn._lines.AddRange(lines);
        return saleReturn;
    }
}
