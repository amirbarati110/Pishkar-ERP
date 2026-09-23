using ERP.Domain.Catalog;
using ERP.Domain.Common;
using ERP.Domain.Inventory;
using ERP.Domain.Sales;

namespace ERP.Domain.Tests.Sales;

/// <summary>
/// «خدمات/هزینه» on a return is the cashier's choice (user decision 1405/07/02): given back with
/// its VAT when chosen, at most once per invoice, and never by default.
/// </summary>
public sealed class SaleReturnServiceChargeTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 10, 0, 0, TimeSpan.Zero);
    private static readonly IReadOnlyDictionary<ProductId, PreviouslyReturned> NothingReturnedYet =
        new Dictionary<ProductId, PreviouslyReturned>();

    /// <summary>
    /// Awkward on purpose, so rounding shows: 3 × 1,234,567 and 2 × 777,777 rials, an invoice
    /// discount of 123,457, a service charge of 55,555 and 9% VAT.
    /// </summary>
    private static (Sale Sale, ProductId First, ProductId Second) CompletedSale(long serviceChargeRials = 55_555)
    {
        var first = ProductId.New();
        var second = ProductId.New();
        var sale = Sale.OpenDraft(WarehouseId.New(), null, Now);
        sale.AddOrIncreaseLine(first, Quantity.Create(3), Money.FromRials(1_234_567));
        sale.AddOrIncreaseLine(second, Quantity.Create(2), Money.FromRials(777_777));
        sale.ApplyDiscount(Money.FromRials(123_457));
        sale.ApplyServiceCharge(Money.FromRials(serviceChargeRials));
        sale.Complete(SaleNumber.From(1258), PaymentMethod.Cash, taxRatePercent: 9, Now);
        return (sale, first, second);
    }

    private static Dictionary<ProductId, Money> Costs(params ProductId[] products) =>
        products.ToDictionary(product => product, _ => Money.FromRials(500_000));

    [Fact]
    public void ReturningEverythingWithTheServiceChargeRefundsTheInvoiceTotalAndItsVatToTheRial()
    {
        var (sale, first, second) = CompletedSale();

        var saleReturn = SaleReturn.Create(
            sale, ReturnNumber.From(1),
            [new ReturnLineRequest(first, 3, ReturnDisposition.ToStock), new ReturnLineRequest(second, 2, ReturnDisposition.ToStock)],
            PaymentMethod.Cash, "لغو سفارش", NothingReturnedYet, Costs(first, second), Now,
            refundServiceCharge: true);

        Assert.Equal(sale.Totals!.Tax.Rials, saleReturn.TotalTax.Rials);
        Assert.Equal(55_555, saleReturn.ServiceCharge.Net.Rials);
        Assert.InRange(sale.Totals.Total.Rials - saleReturn.RefundTotal.Rials, -1, 1); // discount share rounds per line
    }

    [Fact]
    public void WithoutTheChoiceTheServiceChargeIsKept()
    {
        var (sale, first, second) = CompletedSale();

        var saleReturn = SaleReturn.Create(
            sale, ReturnNumber.From(1),
            [new ReturnLineRequest(first, 3, ReturnDisposition.ToStock), new ReturnLineRequest(second, 2, ReturnDisposition.ToStock)],
            PaymentMethod.Cash, "لغو سفارش", NothingReturnedYet, Costs(first, second), Now);

        Assert.True(saleReturn.ServiceCharge.IsEmpty);
        var kept = SaleReturn.PriceServiceCharge(sale, alreadyRefunded: false);
        Assert.Equal(sale.Totals!.Tax.Rials, saleReturn.TotalTax.Rials + kept.Tax.Rials);
    }

    [Fact]
    public void TheServiceChargeAloneCanBeGivenBackWithNoGoods()
    {
        // a delivery that never happened
        var (sale, _, _) = CompletedSale();

        var saleReturn = SaleReturn.Create(
            sale, ReturnNumber.From(1), [], PaymentMethod.Cash, "ارسال انجام نشد",
            NothingReturnedYet, new Dictionary<ProductId, Money>(), Now, refundServiceCharge: true);

        Assert.Empty(saleReturn.Lines);
        Assert.Equal(55_555, saleReturn.TotalNet.Rials);
        Assert.Equal(saleReturn.ServiceCharge.Total, saleReturn.RefundTotal);
        Assert.True(saleReturn.TotalTax.Rials > 0);
    }

    [Fact]
    public void AReturnWithNoGoodsAndNoServiceChargeIsStillRejected()
    {
        var (sale, _, _) = CompletedSale();

        var exception = Assert.Throws<DomainException>(() => SaleReturn.Create(
            sale, ReturnNumber.From(1), [], PaymentMethod.Cash, "هیچ",
            NothingReturnedYet, new Dictionary<ProductId, Money>(), Now));

        Assert.Equal("هیچ کالایی برای مرجوعی انتخاب نشده است.", exception.Message);
    }

    [Fact]
    public void TheServiceChargeGoesBackAtMostOnce()
    {
        var (sale, _, _) = CompletedSale();

        var exception = Assert.Throws<DomainException>(() => SaleReturn.Create(
            sale, ReturnNumber.From(2), [], PaymentMethod.Cash, "دوباره",
            NothingReturnedYet, new Dictionary<ProductId, Money>(), Now,
            refundServiceCharge: true, serviceChargeAlreadyRefunded: true));

        Assert.Contains("قبلاً", exception.Message, StringComparison.Ordinal);
        Assert.True(SaleReturn.RefundableServiceCharge(sale, alreadyRefunded: true).IsEmpty);
    }

    [Fact]
    public void AnInvoiceWithoutAServiceChargeHasNothingToOffer()
    {
        var (sale, first, _) = CompletedSale(serviceChargeRials: 0);

        Assert.True(SaleReturn.RefundableServiceCharge(sale, alreadyRefunded: false).IsEmpty);
        Assert.Throws<DomainException>(() => SaleReturn.Create(
            sale, ReturnNumber.From(1), [new ReturnLineRequest(first, 1, ReturnDisposition.ToStock)],
            PaymentMethod.Cash, "x", NothingReturnedYet, Costs(first), Now, refundServiceCharge: true));
    }

    [Fact]
    public void WhatTheWindowOffersIsWhatAReturnRefunds()
    {
        var (sale, _, _) = CompletedSale();

        var offered = SaleReturn.RefundableServiceCharge(sale, alreadyRefunded: false);
        var refunded = SaleReturn.Create(
            sale, ReturnNumber.From(1), [], PaymentMethod.Cash, "ارسال نشد",
            NothingReturnedYet, new Dictionary<ProductId, Money>(), Now, refundServiceCharge: true).ServiceCharge;

        Assert.Equal(offered, refunded);
    }
}
