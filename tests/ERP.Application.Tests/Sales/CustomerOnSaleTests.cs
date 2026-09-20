using ERP.Application.Customers;
using ERP.Application.Sales;
using ERP.Application.Tests.TestDoubles;
using ERP.Domain.Catalog;
using ERP.Domain.Common;
using ERP.Domain.Customers;
using ERP.Domain.Inventory;
using ERP.Domain.Sales;

namespace ERP.Application.Tests.Sales;

/// <summary>Choosing a customer for an invoice, and the credit policy of §6.7.</summary>
public sealed class CustomerOnSaleTests
{
    [Fact]
    public async Task ACustomerCanBeChosenForAnOpenInvoice()
    {
        var context = new ApplicationTestContext();
        var customer = AddCustomer(context);
        var sale = AddDraft(context, tomans: 100_000);

        var result = await new SetSaleCustomerHandler(context.Sales, context.Customers, context.UnitOfWork)
            .ExecuteAsync(new SetSaleCustomerCommand(sale.Id, customer.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(customer.Id, sale.CustomerId);
        Assert.Equal(1, context.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task AnArchivedCustomerCannotBeChosen()
    {
        var context = new ApplicationTestContext();
        var customer = AddCustomer(context);
        customer.Archive();
        var sale = AddDraft(context, tomans: 100_000);

        var result = await new SetSaleCustomerHandler(context.Sales, context.Customers, context.UnitOfWork)
            .ExecuteAsync(new SetSaleCustomerCommand(sale.Id, customer.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("customers.customer.archived", result.Error?.Code);
        Assert.Null(sale.CustomerId);
    }

    [Fact]
    public async Task ACreditSaleWithoutACustomerIsRejectedBeforeStockMovesOrANumberIsDrawn()
    {
        var context = new ApplicationTestContext();
        var sale = AddDraft(context, tomans: 100_000);

        var result = await Complete(context, sale, PaymentMethod.Credit);

        Assert.False(result.IsSuccess);
        Assert.Equal("sales.sale.customer-required", result.Error?.Code);
        Assert.Equal(0, context.SaleNumbers.IssuedCount);
        Assert.Equal(0, context.StockLedgers.SaveCount);
    }

    [Fact]
    public async Task ACreditSaleWithinTheLimitGoesThrough()
    {
        var context = new ApplicationTestContext();
        var customer = AddCustomer(context, creditLimitTomans: 1_000_000);
        var sale = AddDraft(context, tomans: 500_000, customer.Id);

        var result = await Complete(context, sale, PaymentMethod.Credit);

        Assert.True(result.IsSuccess);
        Assert.Equal(SaleStatus.Completed, sale.Status);
    }

    [Fact]
    public async Task CrossingTheLimitNeedsApprovalAndTheMessageSaysByHowMuch()
    {
        var context = new ApplicationTestContext();
        var customer = AddCustomer(context, creditLimitTomans: 1_000_000);
        context.CustomerLedger.CreditInvoices.Add(
            new CreditInvoice(1200, context.Clock.UtcNow.AddDays(-3), Money.FromTomans(800_000)));
        var sale = AddDraft(context, tomans: 400_000, customer.Id); // ۸۰۰ + ۴۰۰ = ۱٬۲۰۰ > ۱٬۰۰۰

        var result = await Complete(context, sale, PaymentMethod.Credit);

        Assert.False(result.IsSuccess);
        Assert.Equal("sales.credit.requires-approval", result.Error?.Code);
        Assert.Equal(
            "با این فاکتور، بدهی «محمد رضایی» به ۱,۲۰۰,۰۰۰ تومان می‌رسد و از سقف اعتبار (۱,۰۰۰,۰۰۰ تومان) بیشتر می‌شود؛ تأیید مدیر لازم است.",
            result.Error?.Message);
        Assert.Equal(SaleStatus.Draft, sale.Status);
        Assert.Equal(0, context.SaleNumbers.IssuedCount);
    }

    [Fact]
    public async Task AnApprovedOverLimitCreditSaleGoesThroughAndTheApprovalIsAudited()
    {
        var context = new ApplicationTestContext();
        var customer = AddCustomer(context, creditLimitTomans: 1_000_000);
        context.CustomerLedger.CreditInvoices.Add(
            new CreditInvoice(1200, context.Clock.UtcNow.AddDays(-3), Money.FromTomans(800_000)));
        var sale = AddDraft(context, tomans: 400_000, customer.Id);

        var result = await Complete(context, sale, PaymentMethod.Credit, approveOverLimit: true);

        Assert.True(result.IsSuccess);
        Assert.Contains(context.Audit.Entries, entry => entry.Action == "sales.credit.over-limit-approved");
    }

    [Fact]
    public async Task PaymentsReceivedReduceTheDebtTheLimitIsCheckedAgainst()
    {
        var context = new ApplicationTestContext();
        var customer = AddCustomer(context, creditLimitTomans: 1_000_000);
        context.CustomerLedger.CreditInvoices.Add(
            new CreditInvoice(1200, context.Clock.UtcNow.AddDays(-3), Money.FromTomans(800_000)));
        context.CustomerLedger.Payments.Add(Money.FromTomans(500_000)); // بدهی ۳۰۰ مانده
        var sale = AddDraft(context, tomans: 400_000, customer.Id);

        var result = await Complete(context, sale, PaymentMethod.Credit);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task FarBeyondTheLimitIsBlockedEvenWithApproval()
    {
        var context = new ApplicationTestContext();
        var customer = AddCustomer(context, creditLimitTomans: 1_000_000);
        var sale = AddDraft(context, tomans: 2_000_000, customer.Id);

        var result = await Complete(context, sale, PaymentMethod.Credit, approveOverLimit: true);

        Assert.False(result.IsSuccess);
        Assert.Equal("sales.credit.blocked", result.Error?.Code);
    }

    [Fact]
    public async Task AChequeSaleNeedsACustomerButIsNotHeldToTheCreditLimit()
    {
        var context = new ApplicationTestContext();
        var customer = AddCustomer(context, creditLimitTomans: 1_000_000);
        var sale = AddDraft(context, tomans: 2_000_000, customer.Id);

        var result = await Complete(context, sale, PaymentMethod.Cheque);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task TheAccountSummaryShowsDebtAndOpenInvoices()
    {
        var context = new ApplicationTestContext();
        var customer = AddCustomer(context);
        context.CustomerLedger.CreditInvoices.Add(
            new CreditInvoice(1258, context.Clock.UtcNow.AddDays(-2), Money.FromTomans(1_000_000)));
        context.CustomerLedger.CreditInvoices.Add(
            new CreditInvoice(1260, context.Clock.UtcNow.AddDays(-1), Money.FromTomans(850_000)));

        var result = await new GetCustomerAccountHandler(context.Customers, context.CustomerLedger)
            .ExecuteAsync(new GetCustomerAccountQuery(customer.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1_850_000, result.Value!.Debt.ToTomansExact());
        Assert.Equal([1258L, 1260L], result.Value.OpenInvoiceNumbers);
    }

    [Fact]
    public async Task RecordingAPaymentSavesAuditsAndCommits()
    {
        var context = new ApplicationTestContext();
        var customer = AddCustomer(context);
        var payments = new RecordingPayments();

        var result = await new RecordCustomerPaymentHandler(
                context.Customers, payments, context.Audit, context.UnitOfWork, context.User, context.Clock)
            .ExecuteAsync(
                new RecordCustomerPaymentCommand(customer.Id, Money.FromTomans(500_000).Rials, CustomerPaymentMethod.Cash, " قسط اول "),
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        var payment = Assert.Single(payments.Items);
        Assert.Equal("قسط اول", payment.Note);
        Assert.Equal("customers.payment.received", Assert.Single(context.Audit.Entries).Action);
        Assert.Equal(1, context.UnitOfWork.CommitCount);
    }

    private static Customer AddCustomer(ApplicationTestContext context, long creditLimitTomans = 0)
    {
        var customer = Customer.QuickCreate("محمد رضایی", "09123456789");
        customer.SetCreditLimit(Money.FromTomans(creditLimitTomans));
        context.Customers.Items.Add(customer);
        return customer;
    }

    private static Sale AddDraft(ApplicationTestContext context, long tomans, CustomerId? customerId = null)
    {
        var warehouseId = WarehouseId.New();
        var productId = ProductId.New();
        var ledger = StockLedger.Empty(productId, warehouseId);
        ledger.ReceiveOpeningStock(Quantity.Create(10), Money.FromTomans(1_000), DateOnly.FromDateTime(context.Clock.UtcNow.Date));
        context.StockLedgers.Items.Add(ledger);

        var sale = Sale.OpenDraft(warehouseId, customerId, context.Clock.UtcNow);
        sale.AddOrIncreaseLine(productId, Quantity.Create(1), Money.FromTomans(tomans));
        context.Sales.Items.Add(sale);
        return sale;
    }

    private static Task<ERP.Application.Common.Result<CompletedSale>> Complete(
        ApplicationTestContext context,
        Sale sale,
        PaymentMethod method,
        bool approveOverLimit = false)
    {
        return new CompleteSaleHandler(
                context.Sales,
                context.StockLedgers,
                context.SaleLineCosts,
                context.SaleNumbers,
                context.Customers,
                context.CustomerLedger,
                context.Audit,
                context.JournalEntries,
                context.UnitOfWork,
                context.User,
                context.Clock)
            .ExecuteAsync(
                new CompleteSaleCommand(sale.Id, method, 0, TaxRatePercent: 0, ApproveCreditOverLimit: approveOverLimit),
                CancellationToken.None);
    }

    private sealed class RecordingPayments : ICustomerPaymentRepository
    {
        public List<CustomerPayment> Items { get; } = [];

        public Task AddAsync(CustomerPayment payment, CancellationToken cancellationToken)
        {
            Items.Add(payment);
            return Task.CompletedTask;
        }
    }
}
