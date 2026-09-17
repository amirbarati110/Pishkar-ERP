using ERP.Application.Sales;
using ERP.Application.Tests.TestDoubles;
using ERP.Domain.Catalog;
using ERP.Domain.Common;
using ERP.Domain.Customers;
using ERP.Domain.Inventory;
using ERP.Domain.Sales;

namespace ERP.Application.Tests.Sales;

public sealed class SaleCorrectionTests
{
    [Fact]
    public async Task StartFailsWithoutAReason()
    {
        var context = new ApplicationTestContext();
        var original = await CompletedCashSaleAsync(context, discountTomans: 0);

        var result = await StartHandler(context).ExecuteAsync(
            new StartSaleCorrectionCommand(original.Id, "   "), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("sales.correction.reason-required", result.Error?.Code);
    }

    [Fact]
    public async Task StartFailsWhenTheOriginalDoesNotExist()
    {
        var context = new ApplicationTestContext();

        var result = await StartHandler(context).ExecuteAsync(
            new StartSaleCorrectionCommand(SaleId.New(), "دلیل"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("sales.sale.not-found", result.Error?.Code);
    }

    [Fact]
    public async Task StartFailsWhenTheOriginalIsStillADraft()
    {
        var context = new ApplicationTestContext();
        var draft = Sale.OpenDraft(WarehouseId.New(), null, context.Clock.UtcNow);
        context.Sales.Items.Add(draft);

        var result = await StartHandler(context).ExecuteAsync(
            new StartSaleCorrectionCommand(draft.Id, "دلیل"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("sales.correction.not-completed", result.Error?.Code);
    }

    [Fact]
    public async Task StartFailsWhenTheOriginalIsItselfACorrection()
    {
        var context = new ApplicationTestContext();
        var firstOriginal = await CompletedCashSaleAsync(context, discountTomans: 0);
        var correction = await StartAndComplete(context, firstOriginal, "اصلاح اول");

        var result = await StartHandler(context).ExecuteAsync(
            new StartSaleCorrectionCommand(correction.Id, "اصلاح دوم"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("sales.correction.corrects-a-correction", result.Error?.Code);
    }

    [Fact]
    public async Task ACompletedCorrectionPostsItsOwnJournalEntryTaggedAsACorrection()
    {
        // این تست عمداً از CompleteSaleHandler واقعی برای فاکتور اصلی استفاده
        // نمی‌کند (helper مشترک این فایل، Sale.Complete را مستقیم صدا می‌زند) —
        // این‌که خودِ فاکتور اصلی هم سند می‌گیرد، در CompleteSaleTests ثابت شده.
        var context = new ApplicationTestContext();
        var original = await CompletedCashSaleAsync(context, discountTomans: 0);
        var correction = await StartAndComplete(context, original, "روش پرداخت اشتباه بود");

        var correctionEntry = Assert.Single(context.JournalEntries.Items, entry => entry.SourceId == correction.Id.ToString());
        Assert.Equal(ERP.Domain.Accounting.JournalSourceType.SaleCorrection, correctionEntry.SourceType);
        Assert.DoesNotContain(context.JournalEntries.Items, entry => entry.SourceId == original.Id.ToString());
    }

    [Fact]
    public async Task StartFailsWhenTheOriginalAlreadyHasACorrection()
    {
        var context = new ApplicationTestContext();
        var original = await CompletedCashSaleAsync(context, discountTomans: 0);
        await StartAndComplete(context, original, "اصلاح اول");

        var result = await StartHandler(context).ExecuteAsync(
            new StartSaleCorrectionCommand(original.Id, "اصلاح دوباره"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("sales.correction.already-corrected", result.Error?.Code);
    }

    [Fact]
    public async Task StartCopiesLinesAndChargesAndStoresTheReasonAsTheNote()
    {
        var context = new ApplicationTestContext();
        var original = await CompletedCashSaleAsync(context, discountTomans: 10_000);

        var result = await StartHandler(context).ExecuteAsync(
            new StartSaleCorrectionCommand(original.Id, "تخفیف اشتباه بود"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var correction = context.Sales.Items.Single(sale => sale.Id == result.Value);
        Assert.Equal(original.Id, correction.CorrectsSaleId);
        Assert.Equal(SaleStatus.Draft, correction.Status);
        Assert.Equal("تخفیف اشتباه بود", correction.Note);
        Assert.Equal(10_000, correction.Discount.ToTomansExact());
        Assert.Single(correction.Lines);
    }

    [Fact]
    public async Task CompleteFailsWhenTheSaleIsNotACorrection()
    {
        var context = new ApplicationTestContext();
        var ordinary = await CompletedCashSaleAsync(context, discountTomans: 0);

        var result = await CompleteHandler(context).ExecuteAsync(
            new CompleteSaleCorrectionCommand(ordinary.Id, PaymentMethod.Cash, 10), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("sales.correction.not-a-correction", result.Error?.Code);
    }

    [Fact]
    public async Task CompleteDrawsANewNumberWritesAnAuditEntryAndNeverTouchesStock()
    {
        var context = new ApplicationTestContext();
        var original = await CompletedCashSaleAsync(context, discountTomans: 0); // draws number 1258
        var startResult = await StartHandler(context).ExecuteAsync(
            new StartSaleCorrectionCommand(original.Id, "روش پرداخت اشتباه ثبت شده بود"), CancellationToken.None);
        var correction = context.Sales.Items.Single(sale => sale.Id == startResult.Value);
        var stockSavesBeforeCompletion = context.StockLedgers.SaveCount;

        var result = await CompleteHandler(context).ExecuteAsync(
            new CompleteSaleCorrectionCommand(correction.Id, PaymentMethod.Card, TaxRatePercent: 10), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SaleNumber.From(1259), result.Value!.Number); // its own, later number — not the original's
        Assert.Equal(SaleStatus.Completed, correction.Status);
        Assert.Equal(PaymentMethod.Card, correction.PaymentMethod);
        Assert.Equal(stockSavesBeforeCompletion, context.StockLedgers.SaveCount); // unchanged — no FIFO consumption

        var entry = Assert.Single(context.Audit.Entries, e => e.Action == "sales.sale.corrected");
        Assert.Contains($"corrects={original.Id}", entry.NewValue, StringComparison.Ordinal);
        Assert.Contains("روش پرداخت اشتباه ثبت شده بود", entry.NewValue, StringComparison.Ordinal);

        // The original itself is never rewritten.
        Assert.Equal(SaleNumber.From(1258), original.Number);
        Assert.Equal(PaymentMethod.Cash, original.PaymentMethod);
    }

    [Fact]
    public async Task CompleteStillEnforcesTheCreditLimitForANewlyCreditCorrection()
    {
        var context = new ApplicationTestContext();
        var customer = Customer.QuickCreate("محمد رضایی", "09123456789");
        customer.SetCreditLimit(Money.FromTomans(400_000)); // sale below asks for approval, not an outright block (>1.5× limit)
        context.Customers.Items.Add(customer);

        var original = Sale.OpenDraft(WarehouseId.New(), customer.Id, context.Clock.UtcNow);
        original.AddOrIncreaseLine(ProductId.New(), Quantity.Create(1), Money.FromTomans(500_000));
        var originalNumber = await context.SaleNumbers.NextAsync(CancellationToken.None);
        original.Complete(originalNumber, PaymentMethod.Cash, 0, context.Clock.UtcNow);
        context.Sales.Items.Add(original);

        var startResult = await StartHandler(context).ExecuteAsync(
            new StartSaleCorrectionCommand(original.Id, "نسیه شد"), CancellationToken.None);
        var correction = context.Sales.Items.Single(sale => sale.Id == startResult.Value);

        var result = await CompleteHandler(context).ExecuteAsync(
            new CompleteSaleCorrectionCommand(correction.Id, PaymentMethod.Credit, TaxRatePercent: 0), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("sales.credit.requires-approval", result.Error?.Code);
    }

    private static async Task<Sale> CompletedCashSaleAsync(ApplicationTestContext context, long discountTomans)
    {
        var sale = Sale.OpenDraft(WarehouseId.New(), null, context.Clock.UtcNow);
        sale.AddOrIncreaseLine(ProductId.New(), Quantity.Create(1), Money.FromTomans(245_000));
        if (discountTomans > 0)
        {
            sale.ApplyDiscount(Money.FromTomans(discountTomans));
        }

        var number = await context.SaleNumbers.NextAsync(CancellationToken.None);
        sale.Complete(number, PaymentMethod.Cash, taxRatePercent: 0, context.Clock.UtcNow);
        context.Sales.Items.Add(sale);
        return sale;
    }

    private static async Task<Sale> StartAndComplete(ApplicationTestContext context, Sale original, string reason)
    {
        var started = await StartHandler(context).ExecuteAsync(
            new StartSaleCorrectionCommand(original.Id, reason), CancellationToken.None);
        var correction = context.Sales.Items.Single(sale => sale.Id == started.Value);
        await CompleteHandler(context).ExecuteAsync(
            new CompleteSaleCorrectionCommand(correction.Id, PaymentMethod.Cash, TaxRatePercent: 0), CancellationToken.None);
        return correction;
    }

    private static StartSaleCorrectionHandler StartHandler(ApplicationTestContext context) =>
        new(context.Sales, context.UnitOfWork, context.Clock);

    private static CompleteSaleCorrectionHandler CompleteHandler(ApplicationTestContext context) =>
        new(
            context.Sales,
            context.SaleNumbers,
            context.Customers,
            context.CustomerLedger,
            context.Audit,
            context.JournalEntries,
            context.UnitOfWork,
            context.User,
            context.Clock);
}
