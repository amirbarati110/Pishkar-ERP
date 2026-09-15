using ERP.Domain.Common;
using ERP.Domain.Customers;

namespace ERP.Domain.Tests.Customers;

public sealed class CustomerAccountTests
{
    private static readonly DateTimeOffset Day1 = new(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ANewCustomerOwesNothing()
    {
        var account = CustomerAccount.Calculate(Money.Zero, [], []);

        Assert.Equal(0, account.Debt.Rials);
        Assert.Equal(0, account.Advance.Rials);
        Assert.Empty(account.OpenInvoices);
    }

    [Fact]
    public void CreditInvoicesAddUpToTheDebtAndAreAllOpen()
    {
        var account = CustomerAccount.Calculate(
            Money.FromTomans(100_000),
            [Invoice(1258, 0, 1_000_000), Invoice(1260, 1, 750_000)],
            []);

        Assert.Equal(1_850_000, account.Debt.ToTomansExact());
        Assert.Equal([1258L, 1260L], account.OpenInvoices.Select(invoice => invoice.Number));
    }

    [Fact]
    public void PaymentsSettleTheOpeningBalanceFirstThenTheOldestInvoice()
    {
        // مانده اول دوره ۱۰۰ + فاکتور ۱۲۵۸ (۱٬۰۰۰) + فاکتور ۱۲۶۰ (۷۵۰)؛ دریافت ۱٬۱۰۰
        // → اول دوره و ۱۲۵۸ کامل تسویه، ۱۲۶۰ هنوز باز.
        var account = CustomerAccount.Calculate(
            Money.FromTomans(100_000),
            [Invoice(1260, 1, 750_000), Invoice(1258, 0, 1_000_000)],
            [Money.FromTomans(1_100_000)]);

        Assert.Equal(750_000, account.Debt.ToTomansExact());
        Assert.Equal(1260, Assert.Single(account.OpenInvoices).Number);
    }

    [Fact]
    public void APartlyPaidInvoiceIsStillOpen()
    {
        var account = CustomerAccount.Calculate(
            Money.Zero,
            [Invoice(1258, 0, 1_000_000)],
            [Money.FromTomans(400_000), Money.FromTomans(100_000)]);

        Assert.Equal(500_000, account.Debt.ToTomansExact());
        Assert.Single(account.OpenInvoices);
    }

    [Fact]
    public void OverpaymentBecomesAnAdvanceNotANegativeDebt()
    {
        var account = CustomerAccount.Calculate(
            Money.Zero,
            [Invoice(1258, 0, 1_000_000)],
            [Money.FromTomans(1_200_000)]);

        Assert.Equal(0, account.Debt.Rials);
        Assert.Equal(200_000, account.Advance.ToTomansExact());
        Assert.Empty(account.OpenInvoices);
    }

    [Fact]
    public void APaymentCannotBeZero()
    {
        var exception = Assert.Throws<DomainException>(() => CustomerPayment.Receive(
            CustomerId.New(), Money.Zero, CustomerPaymentMethod.Cash, null, Day1));

        Assert.Equal("مبلغ دریافتی باید بیشتر از صفر باشد.", exception.Message);
    }

    private static CreditInvoice Invoice(long number, int dayOffset, long tomans)
    {
        return new CreditInvoice(number, Day1.AddDays(dayOffset), Money.FromTomans(tomans));
    }
}
