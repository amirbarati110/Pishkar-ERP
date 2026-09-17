using ERP.Application.Accounting;
using ERP.Domain.Accounting;
using ERP.Domain.Common;
using ERP.Domain.Sales;

namespace ERP.Application.Tests.Accounting;

public sealed class SaleJournalEntryFactoryTests
{
    private static readonly DateTimeOffset PostedAt = new(2026, 9, 17, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ACashSaleWithNoTaxDebitsCashAndCreditsRevenueOnly()
    {
        var totals = new SaleTotals(Money.FromTomans(245_000), Money.Zero, Money.Zero, Money.Zero, Money.FromTomans(245_000));

        var entry = SaleJournalEntryFactory.Create(JournalSourceType.Sale, "sale-1", totals, PaymentMethod.Cash, PostedAt);

        Assert.NotNull(entry);
        Assert.Equal(2, entry!.Lines.Count);
        var debit = Assert.Single(entry.Lines, line => line.Debit.Rials > 0);
        Assert.Equal(AccountCode.Cash, debit.Account);
        Assert.Equal(2_450_000, debit.Debit.Rials);
        var credit = Assert.Single(entry.Lines, line => line.Credit.Rials > 0);
        Assert.Equal(AccountCode.SalesRevenue, credit.Account);
        Assert.Equal(2_450_000, credit.Credit.Rials);
    }

    [Fact]
    public void ATaxedCardSaleSplitsRevenueAndVatAndDebitsCardClearing()
    {
        var totals = new SaleTotals(
            Money.FromTomans(245_000), Money.Zero, Money.Zero, Money.FromTomans(24_500), Money.FromTomans(269_500));

        var entry = SaleJournalEntryFactory.Create(JournalSourceType.Sale, "sale-2", totals, PaymentMethod.Card, PostedAt);

        Assert.NotNull(entry);
        Assert.Equal(3, entry!.Lines.Count);
        Assert.Contains(entry.Lines, line => line.Account == AccountCode.CardClearing && line.Debit.Rials == 2_695_000);
        Assert.Contains(entry.Lines, line => line.Account == AccountCode.SalesRevenue && line.Credit.Rials == 2_450_000);
        Assert.Contains(entry.Lines, line => line.Account == AccountCode.VatPayable && line.Credit.Rials == 245_000);
    }

    [Fact]
    public void ACreditSaleDebitsAccountsReceivable()
    {
        var totals = new SaleTotals(Money.FromTomans(500_000), Money.Zero, Money.Zero, Money.Zero, Money.FromTomans(500_000));

        var entry = SaleJournalEntryFactory.Create(JournalSourceType.Sale, "sale-3", totals, PaymentMethod.Credit, PostedAt);

        Assert.Contains(entry!.Lines, line => line.Account == AccountCode.AccountsReceivable && line.Debit.Rials == 5_000_000);
    }

    [Fact]
    public void AChequeSaleDebitsChequesReceivable()
    {
        var totals = new SaleTotals(Money.FromTomans(500_000), Money.Zero, Money.Zero, Money.Zero, Money.FromTomans(500_000));

        var entry = SaleJournalEntryFactory.Create(JournalSourceType.Sale, "sale-4", totals, PaymentMethod.Cheque, PostedAt);

        Assert.Contains(entry!.Lines, line => line.Account == AccountCode.ChequesReceivable && line.Debit.Rials == 5_000_000);
    }

    [Fact]
    public void AFullyDiscountedFreeSalePostsNothing()
    {
        var totals = new SaleTotals(Money.FromTomans(100_000), Money.FromTomans(100_000), Money.Zero, Money.Zero, Money.Zero);

        var entry = SaleJournalEntryFactory.Create(JournalSourceType.Sale, "sale-5", totals, PaymentMethod.Cash, PostedAt);

        Assert.Null(entry);
    }
}
