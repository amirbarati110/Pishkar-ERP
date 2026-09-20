using ERP.Application.Sales;
using ERP.Application.Tests.TestDoubles;
using ERP.Domain.Accounting;
using ERP.Domain.Catalog;
using ERP.Domain.Common;
using ERP.Domain.Inventory;
using ERP.Domain.Sales;

namespace ERP.Application.Tests.Sales;

public sealed class CompleteSaleReturnTests
{
    private sealed record Scenario(
        ApplicationTestContext Context,
        Sale Sale,
        ProductId Product,
        WarehouseId Warehouse,
        StockLedger Ledger);

    /// <summary>موجودی ۱۰ عدد با بهای ۵۰٬۰۰۰ تومان؛ فروش ۴ عدد × ۱۰۰٬۰۰۰ تومان نقدی با مالیات ۱۰٪.</summary>
    private static async Task<Scenario> CompletedSaleAsync(bool withCustomer = false)
    {
        var context = new ApplicationTestContext();
        var warehouse = WarehouseId.New();
        var product = ProductId.New();

        var ledger = StockLedger.Empty(product, warehouse);
        ledger.ReceiveOpeningStock(Quantity.Create(10), Money.FromTomans(50_000), DateOnly.FromDateTime(context.Clock.UtcNow.Date));
        context.StockLedgers.Items.Add(ledger);

        var sale = Sale.OpenDraft(warehouse, withCustomer ? ERP.Domain.Customers.CustomerId.New() : null, context.Clock.UtcNow);
        sale.AddOrIncreaseLine(product, Quantity.Create(4), Money.FromTomans(100_000));
        context.Sales.Items.Add(sale);

        var completed = await new CompleteSaleHandler(
                context.Sales, context.StockLedgers, context.SaleLineCosts, context.SaleNumbers, context.Customers,
                context.CustomerLedger, context.Audit, context.JournalEntries, context.UnitOfWork, context.User, context.Clock)
            .ExecuteAsync(new CompleteSaleCommand(sale.Id, PaymentMethod.Cash, 0, 10), CancellationToken.None);
        Assert.True(completed.IsSuccess, completed.Error?.Message);

        return new Scenario(context, sale, product, warehouse, ledger);
    }

    private static CompleteSaleReturnHandler ReturnHandler(ApplicationTestContext context) =>
        new(
            context.Sales, context.SaleReturns, context.SaleLineCosts, context.StockLedgers, context.ReturnNumbers,
            context.Audit, context.JournalEntries, context.UnitOfWork, context.User, context.Clock);

    [Fact]
    public async Task ReturningToStockPutsTheGoodsBackAtTheirOriginalCostAndPostsTheMirrorEntry()
    {
        var scenario = await CompletedSaleAsync();
        var context = scenario.Context;
        Assert.Equal(6, scenario.Ledger.AvailableQuantity.Value);

        var result = await ReturnHandler(context).ExecuteAsync(
            new CompleteSaleReturnCommand(
                scenario.Sale.Id,
                [new ReturnLineRequest(scenario.Product, 1, ReturnDisposition.ToStock)],
                PaymentMethod.Cash,
                "اندازه نبود"),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(ReturnNumber.From(1), result.Value!.Number);
        Assert.Equal(1_100_000, result.Value.Refund.Rials); // ۱٬۰۰۰٬۰۰۰ ریال + ۱۰٪ مالیات

        Assert.Equal(7, scenario.Ledger.AvailableQuantity.Value);
        Assert.Contains(scenario.Ledger.Movements, movement => movement.Type == StockMovementType.Return);
        Assert.Equal(500_000, scenario.Ledger.Layers[^1].UnitCost.Rials); // بهای اصلی، نه امروز

        var entry = Assert.Single(context.JournalEntries.Items, item => item.SourceType == JournalSourceType.SaleReturn);
        Assert.Equal(entry.Lines.Sum(line => line.Debit.Rials), entry.Lines.Sum(line => line.Credit.Rials));
        Assert.Contains(entry.Lines, line => line.Account == AccountCode.SalesReturns && line.Debit.Rials == 1_000_000);
        Assert.Contains(entry.Lines, line => line.Account == AccountCode.VatPayable && line.Debit.Rials == 100_000);
        Assert.Contains(entry.Lines, line => line.Account == AccountCode.Cash && line.Credit.Rials == 1_100_000);
        Assert.Contains(entry.Lines, line => line.Account == AccountCode.Inventory && line.Debit.Rials == 500_000);
        Assert.Contains(entry.Lines, line => line.Account == AccountCode.CostOfGoodsSold && line.Credit.Rials == 500_000);

        Assert.Contains(context.Audit.Entries, audit => audit.Action == "sales.return.completed");
        Assert.Single(context.SaleReturns.Items);
    }

    [Fact]
    public async Task ADamagedReturnRefundsTheMoneyButLeavesTheShelfAndTheCostAlone()
    {
        var scenario = await CompletedSaleAsync();

        var result = await ReturnHandler(scenario.Context).ExecuteAsync(
            new CompleteSaleReturnCommand(
                scenario.Sale.Id,
                [new ReturnLineRequest(scenario.Product, 2, ReturnDisposition.Damaged)],
                PaymentMethod.Card,
                "شکسته"),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(6, scenario.Ledger.AvailableQuantity.Value);
        var entry = Assert.Single(scenario.Context.JournalEntries.Items, item => item.SourceType == JournalSourceType.SaleReturn);
        Assert.DoesNotContain(entry.Lines, line => line.Account == AccountCode.Inventory);
        Assert.DoesNotContain(entry.Lines, line => line.Account == AccountCode.CostOfGoodsSold);
        Assert.Contains(entry.Lines, line => line.Account == AccountCode.CardClearing && line.Credit.Rials == 2_200_000);
    }

    [Fact]
    public async Task RefundingOffTheCustomersDebtCreditsAccountsReceivable()
    {
        var scenario = await CompletedSaleAsync(withCustomer: true);

        var result = await ReturnHandler(scenario.Context).ExecuteAsync(
            new CompleteSaleReturnCommand(
                scenario.Sale.Id,
                [new ReturnLineRequest(scenario.Product, 1, ReturnDisposition.ToStock)],
                PaymentMethod.Credit,
                "برگشت از بدهی"),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        var entry = Assert.Single(scenario.Context.JournalEntries.Items, item => item.SourceType == JournalSourceType.SaleReturn);
        Assert.Contains(entry.Lines, line => line.Account == AccountCode.AccountsReceivable && line.Credit.Rials == 1_100_000);
    }

    [Fact]
    public async Task ReturningMoreThanWasSoldIsRejectedAndChangesNothing()
    {
        var scenario = await CompletedSaleAsync();
        var commitsBefore = scenario.Context.UnitOfWork.CommitCount;

        var result = await ReturnHandler(scenario.Context).ExecuteAsync(
            new CompleteSaleReturnCommand(
                scenario.Sale.Id,
                [new ReturnLineRequest(scenario.Product, 5, ReturnDisposition.ToStock)],
                PaymentMethod.Cash,
                "زیاد"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("sales.return.invalid", result.Error?.Code);
        Assert.Empty(scenario.Context.SaleReturns.Items);
        Assert.Equal(6, scenario.Ledger.AvailableQuantity.Value);
        Assert.Equal(commitsBefore, scenario.Context.UnitOfWork.CommitCount);
        var nextNumber = await scenario.Context.ReturnNumbers.NextAsync(CancellationToken.None);
        Assert.Equal(1, nextNumber.Value); // شماره‌ای مصرف نشد
    }

    [Fact]
    public async Task TwoReturnsThatTogetherEqualTheWholeInvoiceRefundExactlyWhatWasPaid()
    {
        var scenario = await CompletedSaleAsync();
        var handler = ReturnHandler(scenario.Context);

        var first = await handler.ExecuteAsync(
            new CompleteSaleReturnCommand(scenario.Sale.Id, [new ReturnLineRequest(scenario.Product, 1, ReturnDisposition.ToStock)], PaymentMethod.Cash, "اول"),
            CancellationToken.None);
        var second = await handler.ExecuteAsync(
            new CompleteSaleReturnCommand(scenario.Sale.Id, [new ReturnLineRequest(scenario.Product, 3, ReturnDisposition.ToStock)], PaymentMethod.Cash, "دوم"),
            CancellationToken.None);

        Assert.True(first.IsSuccess && second.IsSuccess);
        Assert.Equal(scenario.Sale.Totals!.Total.Rials, first.Value!.Refund.Rials + second.Value!.Refund.Rials);
        Assert.Equal(10, scenario.Ledger.AvailableQuantity.Value);

        var third = await handler.ExecuteAsync(
            new CompleteSaleReturnCommand(scenario.Sale.Id, [new ReturnLineRequest(scenario.Product, 1, ReturnDisposition.ToStock)], PaymentMethod.Cash, "سوم"),
            CancellationToken.None);
        Assert.False(third.IsSuccess);
    }

    [Fact]
    public async Task AnInvoiceThatWasCorrectedCanOnlyBeReturnedThroughItsCorrection()
    {
        var scenario = await CompletedSaleAsync();
        var correction = Sale.OpenCorrection(scenario.Sale, scenario.Context.Clock.UtcNow);
        scenario.Context.Sales.Items.Add(correction);

        var result = await ReturnHandler(scenario.Context).ExecuteAsync(
            new CompleteSaleReturnCommand(scenario.Sale.Id, [new ReturnLineRequest(scenario.Product, 1, ReturnDisposition.ToStock)], PaymentMethod.Cash, "قدیمی"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("sales.return.superseded", result.Error?.Code);
    }

    [Fact]
    public async Task AnInvoiceThatHasBeenReturnedCanNoLongerBeCorrected()
    {
        var scenario = await CompletedSaleAsync();
        await ReturnHandler(scenario.Context).ExecuteAsync(
            new CompleteSaleReturnCommand(scenario.Sale.Id, [new ReturnLineRequest(scenario.Product, 1, ReturnDisposition.ToStock)], PaymentMethod.Cash, "اول"),
            CancellationToken.None);

        var result = await new StartSaleCorrectionHandler(
                scenario.Context.Sales, scenario.Context.SaleReturns, scenario.Context.UnitOfWork, scenario.Context.Clock)
            .ExecuteAsync(new StartSaleCorrectionCommand(scenario.Sale.Id, "اشتباه"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("sales.correction.has-returns", result.Error?.Code);
    }

    [Fact]
    public async Task ThePreviewMatchesTheRefundThatIsThenActuallyGiven()
    {
        var scenario = await CompletedSaleAsync();
        var lines = new[] { new ReturnLineRequest(scenario.Product, 3, ReturnDisposition.ToStock) };

        var preview = await new PreviewSaleReturnHandler(scenario.Context.Sales, scenario.Context.SaleReturns, scenario.Context.SaleLineCosts)
            .ExecuteAsync(new PreviewSaleReturnQuery(scenario.Sale.Id, lines), CancellationToken.None);
        var done = await ReturnHandler(scenario.Context).ExecuteAsync(
            new CompleteSaleReturnCommand(scenario.Sale.Id, lines, PaymentMethod.Cash, "پیش‌نمایش"),
            CancellationToken.None);

        Assert.True(preview.IsSuccess && done.IsSuccess);
        Assert.Equal(preview.Value!.Refund.Rials, done.Value!.Refund.Rials);
    }
}
