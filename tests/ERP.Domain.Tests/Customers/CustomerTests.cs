using ERP.Domain.Common;
using ERP.Domain.Customers;

namespace ERP.Domain.Tests.Customers;

public sealed class CustomerTests
{
    [Fact]
    public void QuickCreateNeedsOnlyNameAndMobile()
    {
        var customer = Customer.QuickCreate("محمد رضایی", "۰۹۱۲۳۴۵۶۷۸۹");

        Assert.Equal("محمد رضایی", customer.Name);
        Assert.Equal("09123456789", customer.Mobile); // ارقام فارسی نرمال می‌شوند
        Assert.Equal(CustomerStatus.Active, customer.Status);
        Assert.Equal(0, customer.CreditLimit.Rials);
        Assert.Equal(0, customer.OpeningBalance.Rials);
    }

    [Theory]
    [InlineData("0912345678")]      // یک رقم کم
    [InlineData("091234567890")]    // یک رقم زیاد
    [InlineData("08123456789")]     // با ۰۹ شروع نمی‌شود
    [InlineData("لطفا تماس بگیرید")]
    public void QuickCreateRejectsAnInvalidMobile(string mobile)
    {
        var exception = Assert.Throws<DomainException>(() => Customer.QuickCreate("محمد رضایی", mobile));

        Assert.Equal("شماره موبایل معتبر نیست؛ نمونه درست: ۰۹۱۲۳۴۵۶۷۸۹", exception.Message);
    }

    [Fact]
    public void QuickCreateRejectsABlankName()
    {
        var exception = Assert.Throws<DomainException>(() => Customer.QuickCreate("  ", "09123456789"));

        Assert.Equal("نام مشتری را وارد کنید.", exception.Message);
    }

    [Fact]
    public void MobileIsNormalizedSoDuplicateCheckCanRelyOnIt()
    {
        var withSpaces = Customer.QuickCreate("علی", " ۰۹۱۲ ۳۴۵ ۶۷۸۹ ");
        var withDashes = Customer.QuickCreate("علی", "0912-345-6789");

        Assert.Equal(withSpaces.Mobile, withDashes.Mobile);
    }

    // §6.7 Credit Policy — سقف اعتبار: هشدار / تایید مدیر / مسدود
    [Fact]
    public void WithoutACreditLimitCreditSalesAreAllowed()
    {
        var customer = Customer.QuickCreate("علی", "09123456789");

        var decision = customer.EvaluateCredit(
            currentBalance: Money.FromTomans(5_000_000),
            newAmount: Money.FromTomans(2_000_000));

        Assert.Equal(CreditDecision.Allowed, decision);
    }

    [Fact]
    public void StayingUnderTheCreditLimitIsAllowed()
    {
        var customer = Customer.QuickCreate("علی", "09123456789");
        customer.SetCreditLimit(Money.FromTomans(10_000_000));

        var decision = customer.EvaluateCredit(Money.FromTomans(3_000_000), Money.FromTomans(4_000_000));

        Assert.Equal(CreditDecision.Allowed, decision);
    }

    [Fact]
    public void CrossingTheCreditLimitNeedsManagerApproval()
    {
        var customer = Customer.QuickCreate("علی", "09123456789");
        customer.SetCreditLimit(Money.FromTomans(10_000_000));

        var decision = customer.EvaluateCredit(Money.FromTomans(9_000_000), Money.FromTomans(2_000_000));

        Assert.Equal(CreditDecision.RequiresApproval, decision);
    }

    [Fact]
    public void GoingFarBeyondTheCreditLimitIsBlocked()
    {
        var customer = Customer.QuickCreate("علی", "09123456789");
        customer.SetCreditLimit(Money.FromTomans(10_000_000));

        // بیش از ۱.۵ برابر سقف → دیگر با تایید مدیر هم باز نمی‌شود
        var decision = customer.EvaluateCredit(Money.FromTomans(14_000_000), Money.FromTomans(4_000_000));

        Assert.Equal(CreditDecision.Blocked, decision);
    }

    [Fact]
    public void AnArchivedCustomerCannotTakeCredit()
    {
        var customer = Customer.QuickCreate("علی", "09123456789");
        customer.Archive();

        var decision = customer.EvaluateCredit(Money.Zero, Money.FromTomans(1_000));

        Assert.Equal(CreditDecision.Blocked, decision);
    }
}
