using ERP.Domain.Catalog;
using ERP.Domain.Common;
using ERP.Domain.Customers;
using ERP.Domain.Inventory;
using ERP.Domain.Sales;
using ERP.Domain.Sales.Events;

namespace ERP.Domain.Tests.Sales;

public sealed class SaleTests
{
    private static readonly WarehouseId Warehouse = WarehouseId.New();
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 10, 0, 0, TimeSpan.Zero);
    private static readonly SaleNumber Number = SaleNumber.From(1258);

    [Fact]
    public void OpenDraftStartsEmptyWithZeroSubtotal()
    {
        var sale = Sale.OpenDraft(Warehouse, customerId: null, Now);

        Assert.Equal(SaleStatus.Draft, sale.Status);
        Assert.Empty(sale.Lines);
        Assert.Equal(0, sale.Subtotal.Rials);
    }

    [Fact]
    public void AddOrIncreaseLineAddsANewLineForANewProduct()
    {
        var sale = Sale.OpenDraft(Warehouse, null, Now);
        var productId = ProductId.New();

        sale.AddOrIncreaseLine(productId, Quantity.Create(2), Money.FromTomans(10_000));

        var line = Assert.Single(sale.Lines);
        Assert.Equal(productId, line.ProductId);
        Assert.Equal(2, line.Quantity.Value);
        Assert.Equal(200_000, line.LineTotal.Rials);
    }

    [Fact]
    public void AddOrIncreaseLineMergesAScanOfAnAlreadyCartedProductIntoQuantity()
    {
        var sale = Sale.OpenDraft(Warehouse, null, Now);
        var productId = ProductId.New();
        sale.AddOrIncreaseLine(productId, Quantity.Create(1), Money.FromTomans(10_000));

        sale.AddOrIncreaseLine(productId, Quantity.Create(2), Money.FromTomans(10_000));

        var line = Assert.Single(sale.Lines);
        Assert.Equal(3, line.Quantity.Value);
    }

    [Fact]
    public void RemoveLineTakesTheProductOutOfTheCart()
    {
        var sale = Sale.OpenDraft(Warehouse, null, Now);
        var productId = ProductId.New();
        sale.AddOrIncreaseLine(productId, Quantity.Create(1), Money.FromTomans(10_000));

        sale.RemoveLine(productId);

        Assert.Empty(sale.Lines);
    }

    [Fact]
    public void RemoveLineThrowsWhenProductIsNotInTheCart()
    {
        var sale = Sale.OpenDraft(Warehouse, null, Now);

        var exception = Assert.Throws<DomainException>(() => sale.RemoveLine(ProductId.New()));

        Assert.Equal("این کالا در سبد فاکتور نیست.", exception.Message);
    }

    [Fact]
    public void ApplyDiscountRejectsADiscountLargerThanTheSubtotal()
    {
        var sale = Sale.OpenDraft(Warehouse, null, Now);
        sale.AddOrIncreaseLine(ProductId.New(), Quantity.Create(1), Money.FromTomans(10_000));

        var exception = Assert.Throws<DomainException>(
            () => sale.ApplyDiscount(Money.FromTomans(10_001)));

        Assert.Equal("تخفیف نمی‌تواند از جمع کالاها بیشتر باشد.", exception.Message);
    }

    [Fact]
    public void CompleteThrowsWhenCartIsEmpty()
    {
        var sale = Sale.OpenDraft(Warehouse, null, Now);

        var exception = Assert.Throws<DomainException>(
            () => sale.Complete(Number, PaymentMethod.Cash, taxRatePercent: 9, Now));

        Assert.Equal("فاکتور خالی است؛ حداقل یک کالا اضافه کنید.", exception.Message);
    }

    [Fact]
    public void CompleteComputesSubtotalDiscountTaxAndTotalInTheOrderTheReferencePosShowsThem()
    {
        // جمع کالاها (Subtotal) → تخفیف (Discount) subtracted → مالیات ۹٪
        // (Iran VAT, per the reference POS screens) applied to what's left →
        // مبلغ قابل پرداخت (Total). Tax is charged on the discounted amount,
        // not the raw subtotal — matching how every reference screenshot lists
        // these four rows in that exact order.
        var sale = Sale.OpenDraft(Warehouse, null, Now);
        sale.AddOrIncreaseLine(ProductId.New(), Quantity.Create(1), Money.FromTomans(1_000_000));
        sale.AddOrIncreaseLine(ProductId.New(), Quantity.Create(2), Money.FromTomans(500_000));
        sale.ApplyDiscount(Money.FromTomans(200_000));

        var totals = sale.Complete(Number, PaymentMethod.Card, taxRatePercent: 9, Now);

        Assert.Equal(2_000_000, totals.Subtotal.ToTomansExact());
        Assert.Equal(200_000, totals.Discount.ToTomansExact());
        Assert.Equal(162_000, totals.Tax.ToTomansExact());
        Assert.Equal(1_962_000, totals.Total.ToTomansExact());
        Assert.Equal(SaleStatus.Completed, sale.Status);
        Assert.Equal(Now, sale.CompletedAtUtc);
    }

    [Fact]
    public void CompleteRaisesSaleCompletedWithTheFinalTotal()
    {
        var sale = Sale.OpenDraft(Warehouse, null, Now);
        sale.AddOrIncreaseLine(ProductId.New(), Quantity.Create(1), Money.FromTomans(100_000));

        var totals = sale.Complete(Number, PaymentMethod.Cash, taxRatePercent: 0, Now);

        var domainEvent = Assert.Single(sale.DomainEvents);
        var completed = Assert.IsType<SaleCompleted>(domainEvent);
        Assert.Equal(sale.Id, completed.SaleId);
        Assert.Equal(totals.Total, completed.Total);
    }

    [Fact]
    public void CannotEditOrCompleteASaleThatIsAlreadyCompleted()
    {
        var sale = Sale.OpenDraft(Warehouse, null, Now);
        sale.AddOrIncreaseLine(ProductId.New(), Quantity.Create(1), Money.FromTomans(100_000));
        sale.Complete(Number, PaymentMethod.Cash, taxRatePercent: 0, Now);

        Assert.Throws<DomainException>(
            () => sale.AddOrIncreaseLine(ProductId.New(), Quantity.Create(1), Money.FromTomans(1)));
        Assert.Throws<DomainException>(() => sale.ApplyDiscount(Money.FromTomans(1)));
        Assert.Throws<DomainException>(() => sale.Complete(Number, PaymentMethod.Cash, 0, Now));
        Assert.Throws<DomainException>(() => sale.Cancel());
    }

    [Fact]
    public void LineDiscountReducesTheLineTotalAndTheInvoiceSubtotal()
    {
        var sale = Sale.OpenDraft(Warehouse, null, Now);
        var productId = ProductId.New();
        sale.AddOrIncreaseLine(productId, Quantity.Create(3), Money.FromTomans(100_000));

        sale.ChangeLine(productId, Quantity.Create(3), Money.FromTomans(100_000), Money.FromTomans(50_000));

        var line = Assert.Single(sale.Lines);
        Assert.Equal(50_000, line.Discount.ToTomansExact());
        Assert.Equal(250_000, line.LineTotal.ToTomansExact()); // ۳×۱۰۰٬۰۰۰ − ۵۰٬۰۰۰
        Assert.Equal(250_000, sale.Subtotal.ToTomansExact());
    }

    [Fact]
    public void LineDiscountCannotExceedTheLineAmount()
    {
        var sale = Sale.OpenDraft(Warehouse, null, Now);
        var productId = ProductId.New();
        sale.AddOrIncreaseLine(productId, Quantity.Create(1), Money.FromTomans(100_000));

        var exception = Assert.Throws<DomainException>(() => sale.ChangeLine(
            productId, Quantity.Create(1), Money.FromTomans(100_000), Money.FromTomans(100_001)));

        Assert.Equal("تخفیف سطر نمی‌تواند از مبلغ همان سطر بیشتر باشد.", exception.Message);
    }

    [Fact]
    public void ChangeLineCanOverrideThePriceForThisInvoiceOnly()
    {
        var sale = Sale.OpenDraft(Warehouse, null, Now);
        var productId = ProductId.New();
        sale.AddOrIncreaseLine(productId, Quantity.Create(2), Money.FromTomans(100_000));

        sale.ChangeLine(productId, Quantity.Create(2), Money.FromTomans(90_000), Money.Zero);

        var line = Assert.Single(sale.Lines);
        Assert.Equal(90_000, line.UnitPrice.ToTomansExact());
        Assert.Equal(100_000, line.CatalogPrice.ToTomansExact()); // قیمت اصلی کالا دست‌نخورده
        Assert.True(line.PriceOverridden);
    }

    [Fact]
    public void ServiceChargeIsAddedAfterDiscountAndIsTaxed()
    {
        var sale = Sale.OpenDraft(Warehouse, null, Now);
        sale.AddOrIncreaseLine(ProductId.New(), Quantity.Create(1), Money.FromTomans(1_000_000));
        sale.ApplyDiscount(Money.FromTomans(200_000));
        sale.ApplyServiceCharge(Money.FromTomans(50_000)); // ارسال/خدمات

        var totals = sale.Complete(Number, PaymentMethod.Cash, taxRatePercent: 9, Now);

        Assert.Equal(1_000_000, totals.Subtotal.ToTomansExact());
        Assert.Equal(200_000, totals.Discount.ToTomansExact());
        Assert.Equal(50_000, totals.ServiceCharge.ToTomansExact());
        Assert.Equal(76_500, totals.Tax.ToTomansExact());   // ۹٪ روی ۸۵۰٬۰۰۰
        Assert.Equal(926_500, totals.Total.ToTomansExact());
    }

    [Fact]
    public void CreditAndChequeAreAcceptedPaymentMethods()
    {
        foreach (var method in new[] { PaymentMethod.Credit, PaymentMethod.Cheque })
        {
            var sale = Sale.OpenDraft(Warehouse, CustomerId.New(), Now);
            sale.AddOrIncreaseLine(ProductId.New(), Quantity.Create(1), Money.FromTomans(10_000));

            sale.Complete(Number, method, taxRatePercent: 0, Now);

            Assert.Equal(method, sale.PaymentMethod);
        }
    }

    [Theory]
    [InlineData(PaymentMethod.Credit)]
    [InlineData(PaymentMethod.Cheque)]
    public void CreditAndChequeSalesNeedACustomer(PaymentMethod method)
    {
        var sale = Sale.OpenDraft(Warehouse, customerId: null, Now);
        sale.AddOrIncreaseLine(ProductId.New(), Quantity.Create(1), Money.FromTomans(10_000));

        var exception = Assert.Throws<DomainException>(() => sale.Complete(Number, method, 0, Now));

        Assert.Equal("برای فروش نسیه یا چکی، اول مشتری را انتخاب کنید.", exception.Message);
        Assert.Equal(SaleStatus.Draft, sale.Status);
    }

    [Fact]
    public void TheCustomerCanBeChosenOrClearedWhileTheCartIsOpenButNotAfterCompletion()
    {
        var sale = Sale.OpenDraft(Warehouse, customerId: null, Now);
        sale.AddOrIncreaseLine(ProductId.New(), Quantity.Create(1), Money.FromTomans(10_000));
        var customer = CustomerId.New();

        sale.AssignCustomer(customer);
        Assert.Equal(customer, sale.CustomerId);

        sale.AssignCustomer(null);
        Assert.Null(sale.CustomerId);

        sale.Complete(Number, PaymentMethod.Cash, 0, Now);
        Assert.Throws<DomainException>(() => { sale.AssignCustomer(customer); });
    }

    [Fact]
    public void CompletionFixesTheTaxSoTheInvoiceKeepsItsTotalsAndPreviewAgrees()
    {
        var sale = Sale.OpenDraft(Warehouse, null, Now);
        sale.AddOrIncreaseLine(ProductId.New(), Quantity.Create(1), Money.FromTomans(1_000_000));
        Assert.Null(sale.Totals);

        var preview = sale.PreviewTotals(9);
        var completed = sale.Complete(Number, PaymentMethod.Cash, 9, Now);

        Assert.Equal(preview, completed);
        Assert.Equal(completed, sale.Totals);
        Assert.Equal(90_000, sale.Tax!.Value.ToTomansExact());
    }

    [Fact]
    public void ADraftHasNoInvoiceNumberUntilItIsCompleted()
    {
        var sale = Sale.OpenDraft(Warehouse, null, Now);
        sale.AddOrIncreaseLine(ProductId.New(), Quantity.Create(1), Money.FromTomans(10_000));

        Assert.Null(sale.Number); // سبد معلق/رهاشده شماره مصرف نمی‌کند

        sale.Complete(Number, PaymentMethod.Cash, taxRatePercent: 0, Now);

        Assert.Equal(Number, sale.Number);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void SaleNumberMustBePositive(long value)
    {
        var exception = Assert.Throws<DomainException>(() => SaleNumber.From(value));

        Assert.Equal("شماره فاکتور باید بزرگ‌تر از صفر باشد.", exception.Message);
    }

    [Fact]
    public void SaleNumberIsShownWithPersianDigits()
    {
        Assert.Equal("۱۲۵۸", SaleNumber.From(1258).ToPersianString());
        Assert.Equal("1258", SaleNumber.From(1258).ToString());
    }

    [Fact]
    public void CancelMovesADraftSaleToCancelledStatus()
    {
        var sale = Sale.OpenDraft(Warehouse, null, Now);

        sale.Cancel();

        Assert.Equal(SaleStatus.Cancelled, sale.Status);
    }
}
