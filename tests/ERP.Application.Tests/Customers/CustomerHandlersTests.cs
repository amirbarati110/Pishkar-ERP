using ERP.Application.Common;
using ERP.Application.Customers;
using ERP.Application.Tests.TestDoubles;
using ERP.Domain.Customers;

namespace ERP.Application.Tests.Customers;

public sealed class CustomerHandlersTests
{
    [Fact]
    public async Task QuickCreateSavesAuditsAndCommits()
    {
        var context = new ApplicationTestContext();
        var customers = new CustomerRepository();

        var result = await CreateHandler(context, customers).ExecuteAsync(
            new QuickCreateCustomerCommand("محمد رضایی", "۰۹۱۲ ۳۴۵ ۶۷۸۹"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var saved = Assert.Single(customers.Items);
        Assert.Equal(result.Value, saved.Id);
        Assert.Equal("09123456789", saved.Mobile);
        Assert.Equal("customers.customer.created", Assert.Single(context.Audit.Entries).Action);
        Assert.Equal(1, context.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task QuickCreateRejectsAMobileAlreadyRegisteredEvenWhenTypedDifferently()
    {
        var context = new ApplicationTestContext();
        var customers = new CustomerRepository();
        customers.Items.Add(Customer.QuickCreate("محمد رضایی", "09123456789"));

        var result = await CreateHandler(context, customers).ExecuteAsync(
            new QuickCreateCustomerCommand("م. رضایی", "۰۹۱۲-۳۴۵-۶۷۸۹"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("customers.customer.duplicate-mobile", result.Error?.Code);
        Assert.Equal(
            "این شماره موبایل قبلاً برای «محمد رضایی» ثبت شده است؛ همان مشتری را انتخاب کنید.",
            result.Error?.Message);
        Assert.Single(customers.Items);
        Assert.Equal(0, context.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task QuickCreateReportsAnInvalidMobileWithoutSaving()
    {
        var context = new ApplicationTestContext();
        var customers = new CustomerRepository();

        var result = await CreateHandler(context, customers).ExecuteAsync(
            new QuickCreateCustomerCommand("علی", "12345"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("customers.customer.invalid", result.Error?.Code);
        Assert.Empty(customers.Items);
    }

    [Theory]
    [InlineData("۰۹۱۲", "0912")]          // ارقام فارسی
    [InlineData("0912 345", "0912345")]    // با فاصله
    [InlineData("علی", null)]              // اسم → موبایل جست‌وجو نمی‌شود
    [InlineData("علی ۲", null)]            // اسم با عدد، باز هم اسم است
    [InlineData("۹۱", null)]               // کمتر از ۳ رقم: تقریباً همه‌ی شماره‌ها را می‌گیرد
    public async Task SearchSendsMobileDigitsOnlyForAPhoneFragment(string term, string? expectedDigits)
    {
        var reader = new RecordingCustomerSearchReader();

        await new SearchCustomersHandler(reader).ExecuteAsync(
            new SearchCustomersQuery($"  {term}  "),
            CancellationToken.None);

        Assert.Equal(term, reader.NameTerm);
        Assert.Equal(expectedDigits, reader.MobileDigits);
    }

    [Fact]
    public async Task SearchWithABlankTermDoesNotHitStorage()
    {
        var reader = new RecordingCustomerSearchReader();

        var results = await new SearchCustomersHandler(reader).ExecuteAsync(
            new SearchCustomersQuery("   "),
            CancellationToken.None);

        Assert.Empty(results);
        Assert.Equal(0, reader.CallCount);
    }

    [Fact]
    public async Task SearchCapsTheResultCount()
    {
        var reader = new RecordingCustomerSearchReader();

        await new SearchCustomersHandler(reader).ExecuteAsync(
            new SearchCustomersQuery("علی", MaxResults: 10_000),
            CancellationToken.None);

        Assert.Equal(50, reader.MaxResults);
    }

    private static QuickCreateCustomerHandler CreateHandler(
        ApplicationTestContext context,
        CustomerRepository customers)
    {
        return new QuickCreateCustomerHandler(
            customers,
            context.Audit,
            context.UnitOfWork,
            context.User,
            context.Clock);
    }

    private sealed class RecordingCustomerSearchReader : ICustomerSearchReader
    {
        public int CallCount { get; private set; }

        public string? NameTerm { get; private set; }

        public string? MobileDigits { get; private set; }

        public int MaxResults { get; private set; }

        public Task<IReadOnlyList<CustomerSearchResult>> SearchAsync(
            string nameTerm,
            string? mobileDigits,
            int maxResults,
            CancellationToken cancellationToken)
        {
            CallCount++;
            NameTerm = nameTerm;
            MobileDigits = mobileDigits;
            MaxResults = maxResults;
            return Task.FromResult<IReadOnlyList<CustomerSearchResult>>([]);
        }
    }
}
