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

    public Money TotalNet => _lines.Aggregate(Money.Zero, (total, line) => total.Add(line.Net));

    public Money TotalTax => _lines.Aggregate(Money.Zero, (total, line) => total.Add(line.Tax));

    /// <summary>What goes back to the customer.</summary>
    public Money RefundTotal => TotalNet.Add(TotalTax);

    public Money RestockCost => _lines.Aggregate(Money.Zero, (total, line) => total.Add(line.RestockCost));

    /// <summary>
    /// Prices what would be refunded without creating anything — the screen
    /// shows this while the cashier picks quantities, and
    /// <see cref="Create"/> uses the very same code, so the number on screen
    /// is the number that gets refunded.
    /// </summary>
    public static IReadOnlyList<SaleReturnLine> Price(
        Sale sale,
        IReadOnlyCollection<ReturnLineRequest> requests,
        IReadOnlyDictionary<ProductId, PreviouslyReturned> previouslyReturned,
        IReadOnlyDictionary<ProductId, Money> unitCosts)
    {
        ArgumentNullException.ThrowIfNull(sale);
        ArgumentNullException.ThrowIfNull(requests);

        if (sale.Status != SaleStatus.Completed || sale.Number is null)
        {
            throw new DomainException("فقط از فاکتور ثبت‌شده می‌شود مرجوعی گرفت.");
        }

        if (requests.Count == 0)
        {
            throw new DomainException("هیچ کالایی برای مرجوعی انتخاب نشده است.");
        }

        if (requests.Select(request => request.ProductId).Distinct().Count() != requests.Count)
        {
            throw new DomainException("یک کالا نمی‌تواند دو بار در یک مرجوعی بیاید.");
        }

        var subtotal = sale.Subtotal.Rials;
        var discountFactor = subtotal > 0 ? (decimal)sale.Discount.Rials / subtotal : 0m;
        var taxBase = subtotal - sale.Discount.Rials + sale.ServiceCharge.Rials;
        var taxFactor = taxBase > 0 && sale.Tax is { } tax ? (decimal)tax.Rials / taxBase : 0m;

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

            var lineNet = saleLine.LineTotal;
            var taxableFull = Money.FromRials(lineNet.Rials - lineNet.Multiply(discountFactor).Rials);
            var taxFull = taxableFull.Multiply(taxFactor);

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

    public static SaleReturn Create(
        Sale sale,
        ReturnNumber number,
        IReadOnlyCollection<ReturnLineRequest> requests,
        PaymentMethod refundMethod,
        string? reason,
        IReadOnlyDictionary<ProductId, PreviouslyReturned> previouslyReturned,
        IReadOnlyDictionary<ProductId, Money> unitCosts,
        DateTimeOffset completedAtUtc)
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

        var lines = Price(sale, requests, previouslyReturned, unitCosts);

        var saleReturn = new SaleReturn(
            SaleReturnId.New(),
            sale.Id,
            sale.WarehouseId,
            sale.CustomerId,
            number,
            refundMethod,
            trimmedReason,
            completedAtUtc);
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
        IEnumerable<SaleReturnLine> lines)
    {
        var saleReturn = new SaleReturn(id, saleId, warehouseId, customerId, number, refundMethod, reason, completedAtUtc);
        saleReturn._lines.AddRange(lines);
        return saleReturn;
    }
}
