using ERP.Domain.Catalog;
using ERP.Domain.Common;
using ERP.Domain.Customers;
using ERP.Domain.Inventory;
using ERP.Domain.Sales;

namespace ERP.Domain.Tests.Sales;

public sealed class SaleReturnTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 10, 0, 0, TimeSpan.Zero);
    private static readonly ReturnNumber Number = ReturnNumber.From(1);
    private static readonly IReadOnlyDictionary<ProductId, PreviouslyReturned> NothingReturnedYet =
        new Dictionary<ProductId, PreviouslyReturned>();

    /// <summary>۳ × ۱۰۰٬۰۰۰ تومان، تخفیف فاکتور ۳۰٬۰۰۰ تومان، مالیات ۱۰٪ ⇒ قابل پرداخت ۲٬۹۷۰٬۰۰۰ ریال.</summary>
    private static (Sale Sale, ProductId Product) CompletedSale(CustomerId? customer = null)
    {
        var product = ProductId.New();
        var sale = Sale.OpenDraft(WarehouseId.New(), customer, Now);
        sale.AddOrIncreaseLine(product, Quantity.Create(3), Money.FromTomans(100_000));
        sale.ApplyDiscount(Money.FromTomans(30_000));
        sale.Complete(SaleNumber.From(1258), PaymentMethod.Cash, taxRatePercent: 10, Now);
        return (sale, product);
    }

    private static Dictionary<ProductId, Money> CostOf(ProductId product) =>
        new() { [product] = Money.FromTomans(60_000) };

    [Fact]
    public void ReturningTheWholeInvoiceRefundsExactlyWhatWasPaid()
    {
        var (sale, product) = CompletedSale();

        var saleReturn = SaleReturn.Create(
            sale, Number, [new ReturnLineRequest(product, 3, ReturnDisposition.ToStock)],
            PaymentMethod.Cash, "ناسازگار", NothingReturnedYet, CostOf(product), Now);

        Assert.Equal(27_000_000 / 10, saleReturn.TotalNet.Rials);
        Assert.Equal(sale.Totals!.Total.Rials, saleReturn.RefundTotal.Rials);
        Assert.Equal(sale.Totals.Tax.Rials, saleReturn.TotalTax.Rials);
    }

    [Fact]
    public void APartialReturnRefundsItsProportionalShareOfDiscountAndTax()
    {
        var (sale, product) = CompletedSale();

        var saleReturn = SaleReturn.Create(
            sale, Number, [new ReturnLineRequest(product, 1, ReturnDisposition.ToStock)],
            PaymentMethod.Cash, "اندازه نبود", NothingReturnedYet, CostOf(product), Now);

        Assert.Equal(900_000, saleReturn.TotalNet.Rials);
        Assert.Equal(90_000, saleReturn.TotalTax.Rials);
        Assert.Equal(990_000, saleReturn.RefundTotal.Rials);
    }

    [Fact]
    public void TwoPartialReturnsTogetherRefundTheWholeLineWithNoRoundingDrift()
    {
        var (sale, product) = CompletedSale();

        var first = SaleReturn.Create(
            sale, Number, [new ReturnLineRequest(product, 1, ReturnDisposition.ToStock)],
            PaymentMethod.Cash, "اول", NothingReturnedYet, CostOf(product), Now);
        var afterFirst = new Dictionary<ProductId, PreviouslyReturned>
        {
            [product] = new PreviouslyReturned(1, first.TotalNet, first.TotalTax),
        };

        var second = SaleReturn.Create(
            sale, ReturnNumber.From(2), [new ReturnLineRequest(product, 2, ReturnDisposition.ToStock)],
            PaymentMethod.Cash, "دوم", afterFirst, CostOf(product), Now);

        Assert.Equal(sale.Totals!.Total.Rials, first.RefundTotal.Rials + second.RefundTotal.Rials);
    }

    [Fact]
    public void ReturningMoreThanWhatIsLeftIsRejectedWithTheRemainingCount()
    {
        var (sale, product) = CompletedSale();
        var alreadyReturned = new Dictionary<ProductId, PreviouslyReturned>
        {
            [product] = new PreviouslyReturned(2, Money.FromRials(1_800_000), Money.FromRials(180_000)),
        };

        var exception = Assert.Throws<DomainException>(() => SaleReturn.Create(
            sale, Number, [new ReturnLineRequest(product, 2, ReturnDisposition.ToStock)],
            PaymentMethod.Cash, "زیاد", alreadyReturned, CostOf(product), Now));

        Assert.Contains("از این کالا فقط ۱ عدد", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ALineThatIsAlreadyFullyReturnedSaysSo()
    {
        var (sale, product) = CompletedSale();
        var alreadyReturned = new Dictionary<ProductId, PreviouslyReturned>
        {
            [product] = new PreviouslyReturned(3, Money.FromRials(2_700_000), Money.FromRials(270_000)),
        };

        var exception = Assert.Throws<DomainException>(() => SaleReturn.Create(
            sale, Number, [new ReturnLineRequest(product, 1, ReturnDisposition.ToStock)],
            PaymentMethod.Cash, "دوباره", alreadyReturned, CostOf(product), Now));

        Assert.Contains("قبلاً مرجوع شده", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AProductThatWasNeverOnTheInvoiceCannotBeReturned()
    {
        var (sale, _) = CompletedSale();

        Assert.Throws<DomainException>(() => SaleReturn.Create(
            sale, Number, [new ReturnLineRequest(ProductId.New(), 1, ReturnDisposition.Damaged)],
            PaymentMethod.Cash, "بیگانه", NothingReturnedYet, new Dictionary<ProductId, Money>(), Now));
    }

    [Fact]
    public void ADraftInvoiceCannotBeReturned()
    {
        var product = ProductId.New();
        var draft = Sale.OpenDraft(WarehouseId.New(), null, Now);
        draft.AddOrIncreaseLine(product, Quantity.Create(1), Money.FromTomans(10_000));

        Assert.Throws<DomainException>(() => SaleReturn.Create(
            draft, Number, [new ReturnLineRequest(product, 1, ReturnDisposition.ToStock)],
            PaymentMethod.Cash, "پیش‌نویس", NothingReturnedYet, CostOf(product), Now));
    }

    [Fact]
    public void ARefundCannotBeMadeByCheque()
    {
        var (sale, product) = CompletedSale();

        Assert.Throws<DomainException>(() => SaleReturn.Create(
            sale, Number, [new ReturnLineRequest(product, 1, ReturnDisposition.ToStock)],
            PaymentMethod.Cheque, "چک", NothingReturnedYet, CostOf(product), Now));
    }

    [Fact]
    public void ReducingTheCustomersDebtNeedsAnInvoiceThatHasACustomer()
    {
        var (sale, product) = CompletedSale(customer: null);

        Assert.Throws<DomainException>(() => SaleReturn.Create(
            sale, Number, [new ReturnLineRequest(product, 1, ReturnDisposition.ToStock)],
            PaymentMethod.Credit, "بدهی", NothingReturnedYet, CostOf(product), Now));
    }

    [Fact]
    public void AReasonIsRequired()
    {
        var (sale, product) = CompletedSale();

        Assert.Throws<DomainException>(() => SaleReturn.Create(
            sale, Number, [new ReturnLineRequest(product, 1, ReturnDisposition.ToStock)],
            PaymentMethod.Cash, "   ", NothingReturnedYet, CostOf(product), Now));
    }

    [Fact]
    public void GoodsWithNoKnownCostCanOnlyBeReturnedAsDamagedOrWaste()
    {
        var (sale, product) = CompletedSale();
        var noCosts = new Dictionary<ProductId, Money>();

        Assert.Throws<DomainException>(() => SaleReturn.Create(
            sale, Number, [new ReturnLineRequest(product, 1, ReturnDisposition.ToStock)],
            PaymentMethod.Cash, "بی‌بها", NothingReturnedYet, noCosts, Now));

        var damaged = SaleReturn.Create(
            sale, Number, [new ReturnLineRequest(product, 1, ReturnDisposition.Damaged)],
            PaymentMethod.Cash, "خراب", NothingReturnedYet, noCosts, Now);
        Assert.Equal(0, damaged.RestockCost.Rials);
    }

    [Fact]
    public void OnlyRowsGoingBackToStockCarryARestockCost()
    {
        var product = ProductId.New();
        var other = ProductId.New();
        var sale = Sale.OpenDraft(WarehouseId.New(), null, Now);
        sale.AddOrIncreaseLine(product, Quantity.Create(2), Money.FromTomans(50_000));
        sale.AddOrIncreaseLine(other, Quantity.Create(2), Money.FromTomans(50_000));
        sale.Complete(SaleNumber.From(1), PaymentMethod.Cash, 0, Now);
        var costs = new Dictionary<ProductId, Money>
        {
            [product] = Money.FromTomans(30_000),
            [other] = Money.FromTomans(30_000),
        };

        var saleReturn = SaleReturn.Create(
            sale, Number,
            [
                new ReturnLineRequest(product, 2, ReturnDisposition.ToStock),
                new ReturnLineRequest(other, 2, ReturnDisposition.Waste),
            ],
            PaymentMethod.Card, "ترکیبی", NothingReturnedYet, costs, Now);

        Assert.Equal(Money.FromTomans(60_000).Rials, saleReturn.RestockCost.Rials);
        Assert.Equal(Money.FromTomans(200_000).Rials, saleReturn.RefundTotal.Rials);
    }

    [Fact]
    public void ReceivingAReturnPutsAFreshLayerBackAtTheOriginalCost()
    {
        var product = ProductId.New();
        var warehouse = WarehouseId.New();
        var ledger = StockLedger.Empty(product, warehouse);
        ledger.ReceiveOpeningStock(Quantity.Create(5), Money.FromTomans(40_000), new DateOnly(2026, 9, 1));
        ledger.ConsumeFifo(Quantity.Create(5), "sale:x");
        Assert.Equal(0, ledger.AvailableQuantity.Value);

        ledger.ReceiveReturn(Quantity.Create(2), Money.FromTomans(40_000), new DateOnly(2026, 9, 14), "return:1");

        Assert.Equal(2, ledger.AvailableQuantity.Value);
        Assert.Contains(ledger.Movements, movement => movement.Type == StockMovementType.Return && movement.Quantity.Value == 2);
        var resold = ledger.ConsumeFifo(Quantity.Create(2), "sale:y");
        Assert.Equal(Money.FromTomans(40_000).Rials, Assert.Single(resold).UnitCost.Rials);
    }
}
