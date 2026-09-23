using ERP.Application.Sales;
using ERP.Application.Tests.TestDoubles;
using ERP.Domain.Accounting;
using ERP.Domain.Catalog;
using ERP.Domain.Common;
using ERP.Domain.Customers;
using ERP.Domain.Inventory;
using ERP.Domain.Sales;

namespace ERP.Application.Tests.Sales;

/// <summary>«خدمات/هزینه هم پس داده شود» through the use cases: refund, VAT, journal and «only once».</summary>
public sealed class ServiceChargeReturnTests
{
    /// <summary>
    /// 4 × 100,000 toman, 20,000 toman delivery, 10% VAT, on credit:
    /// (4,000,000 + 200,000) × 1.1 = 4,620,000 rials.
    /// </summary>
    private static async Task<(ApplicationTestContext Context, Sale Sale, ProductId Product)> CompletedSaleAsync()
    {
        var context = new ApplicationTestContext();
        var warehouse = WarehouseId.New();
        var product = ProductId.New();
        var ledger = StockLedger.Empty(product, warehouse);
        ledger.ReceiveOpeningStock(Quantity.Create(10), Money.FromTomans(50_000), DateOnly.FromDateTime(context.Clock.UtcNow.Date));
        context.StockLedgers.Items.Add(ledger);

        var customer = Customer.QuickCreate("علی", "09121234567");
        context.Customers.Items.Add(customer);
        var sale = Sale.OpenDraft(warehouse, customer.Id, context.Clock.UtcNow);
        sale.AddOrIncreaseLine(product, Quantity.Create(4), Money.FromTomans(100_000));
        sale.ApplyServiceCharge(Money.FromTomans(20_000));
        context.Sales.Items.Add(sale);

        var completed = await new CompleteSaleHandler(
                context.Sales, context.StockLedgers, context.SaleLineCosts, context.SaleNumbers, context.Customers,
                context.CustomerLedger, context.Audit, context.JournalEntries, context.UnitOfWork, context.User, context.Clock)
            .ExecuteAsync(new CompleteSaleCommand(sale.Id, PaymentMethod.Credit, 0, 10, ApproveCreditOverLimit: true), CancellationToken.None);
        Assert.True(completed.IsSuccess, completed.Error?.Message);
        Assert.Equal(4_620_000, completed.Value!.Totals.Total.Rials);
        return (context, sale, product);
    }

    private static CompleteSaleReturnHandler Handler(ApplicationTestContext context) =>
        new(
            context.Sales, context.SaleReturns, context.SaleLineCosts, context.StockLedgers, context.ReturnNumbers,
            context.Audit, context.JournalEntries, context.UnitOfWork, context.User, context.Clock);

    [Fact]
    public async Task ChoosingItGivesBackTheServiceChargeWithItsVatAndPostsItAsAReturn()
    {
        var (context, sale, product) = await CompletedSaleAsync();

        var result = await Handler(context).ExecuteAsync(
            new CompleteSaleReturnCommand(
                sale.Id, [new ReturnLineRequest(product, 4, ReturnDisposition.ToStock)], PaymentMethod.Credit, "لغو سفارش",
                RefundServiceCharge: true),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(4_620_000, result.Value!.Refund.Rials); // everything, delivery included

        var entry = Assert.Single(context.JournalEntries.Items, item => item.SourceType == JournalSourceType.SaleReturn);
        Assert.Contains(entry.Lines, line => line.Account == AccountCode.SalesReturns && line.Debit.Rials == 4_200_000);
        Assert.Contains(entry.Lines, line => line.Account == AccountCode.VatPayable && line.Debit.Rials == 420_000);
        Assert.Contains(entry.Lines, line => line.Account == AccountCode.AccountsReceivable && line.Credit.Rials == 4_620_000);
    }

    [Fact]
    public async Task NotChoosingItKeepsTheServiceCharge()
    {
        var (context, sale, product) = await CompletedSaleAsync();

        var result = await Handler(context).ExecuteAsync(
            new CompleteSaleReturnCommand(
                sale.Id, [new ReturnLineRequest(product, 4, ReturnDisposition.ToStock)], PaymentMethod.Credit, "لغو سفارش"),
            CancellationToken.None);

        Assert.Equal(4_400_000, result.Value!.Refund.Rials); // goods + their VAT; 220,000 delivery+VAT stays owed
        Assert.True(Assert.Single(context.SaleReturns.Items).ServiceCharge.IsEmpty);
    }

    [Fact]
    public async Task TheServiceChargeIsGivenBackOnlyOnceEvenAcrossReturns()
    {
        var (context, sale, product) = await CompletedSaleAsync();
        var handler = Handler(context);

        var first = await handler.ExecuteAsync(
            new CompleteSaleReturnCommand(sale.Id, [], PaymentMethod.Credit, "ارسال انجام نشد", RefundServiceCharge: true),
            CancellationToken.None);
        var second = await handler.ExecuteAsync(
            new CompleteSaleReturnCommand(
                sale.Id, [new ReturnLineRequest(product, 1, ReturnDisposition.ToStock)], PaymentMethod.Credit, "دوباره",
                RefundServiceCharge: true),
            CancellationToken.None);

        Assert.True(first.IsSuccess, first.Error?.Message);
        Assert.Equal(220_000, first.Value!.Refund.Rials); // 200,000 + 10% — a service-only return
        Assert.False(second.IsSuccess);
        Assert.Equal("sales.return.invalid", second.Error?.Code);
        Assert.Single(context.SaleReturns.Items);
        Assert.Equal(2, context.UnitOfWork.CommitCount); // the sale, then only the first return
    }

    [Fact]
    public async Task ThePreviewShowsTheSameFigureTheReturnRefunds()
    {
        var (context, sale, product) = await CompletedSaleAsync();
        var picked = new[] { new ReturnLineRequest(product, 1, ReturnDisposition.ToStock) };

        var preview = await new PreviewSaleReturnHandler(context.Sales, context.SaleReturns, context.SaleLineCosts)
            .ExecuteAsync(new PreviewSaleReturnQuery(sale.Id, picked, RefundServiceCharge: true), CancellationToken.None);
        var done = await Handler(context).ExecuteAsync(
            new CompleteSaleReturnCommand(sale.Id, picked, PaymentMethod.Credit, "یکی خراب", RefundServiceCharge: true),
            CancellationToken.None);

        Assert.Equal(1_320_000, preview.Value!.Refund.Rials); // 1,100,000 goods + 220,000 delivery
        Assert.Equal(preview.Value.Refund, done.Value!.Refund);
    }
}
