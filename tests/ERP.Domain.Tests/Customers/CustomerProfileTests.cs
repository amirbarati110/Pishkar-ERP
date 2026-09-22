using ERP.Domain.Common;
using ERP.Domain.Customers;

namespace ERP.Domain.Tests.Customers;

public sealed class IranianIdentifiersTests
{
    // Numbers that are known to be valid, checked against the published algorithms (checklist «ن-۲»).
    [Theory]
    [InlineData("0499370899")]
    [InlineData("0084575948")]
    public void AValidNationalCodeIsAccepted(string code) => Assert.True(IranianIdentifiers.IsValidNationalCode(code));

    [Theory]
    [InlineData("0499370898")]   // رقم آخر اشتباه
    [InlineData("1234567890")]
    [InlineData("1111111111")]   // همه‌ی رقم‌ها یکی
    [InlineData("049937089")]    // ۹ رقم
    [InlineData("04993708999")]  // ۱۱ رقم
    [InlineData("04993708a9")]
    [InlineData("")]
    public void AnInvalidNationalCodeIsRejected(string code) => Assert.False(IranianIdentifiers.IsValidNationalCode(code));

    [Theory]
    [InlineData("10380284790")]
    [InlineData("14003778990")]
    public void AValidLegalIdIsAccepted(string id) => Assert.True(IranianIdentifiers.IsValidLegalId(id));

    [Theory]
    [InlineData("10380284791")]  // رقم آخر اشتباه
    [InlineData("10100000000")]
    [InlineData("1038028479")]   // ۱۰ رقم
    [InlineData("103802847900")] // ۱۲ رقم
    [InlineData("")]
    public void AnInvalidLegalIdIsRejected(string id) => Assert.False(IranianIdentifiers.IsValidLegalId(id));
}

public sealed class CustomerProfileTests
{
    private static CustomerProfileInput Person(
        string? first = "محمد",
        string? last = "رضایی",
        string? mobile = "09123456789",
        string? nationalId = null,
        string? economic = null,
        string? postal = null,
        string? phone = null,
        string? email = null,
        string? birth = null,
        CustomerKind kind = CustomerKind.Individual) =>
        new(kind, first, last, null, mobile, nationalId, economic, null, postal, phone, email, birth, null, null);

    private static IReadOnlyList<CustomerFieldError> ErrorsOf(CustomerProfileInput input)
    {
        Assert.False(CustomerProfile.TryCreate(input, out var profile, out var errors));
        Assert.Null(profile);
        return errors;
    }

    [Fact]
    public void APersonIsShownAsFirstNameThenLastName()
    {
        var profile = CustomerProfile.Create(Person());

        Assert.Equal("محمد رضایی", profile.DisplayName);
        Assert.Equal("09123456789", profile.Mobile);
    }

    [Fact]
    public void AFirstNameAloneIsEnoughSoTheTillCanKeepUsingQuickCreate()
    {
        Assert.Equal("علی", CustomerProfile.Create(Person(first: "علی", last: null)).DisplayName);
        Assert.Equal("رضایی", CustomerProfile.Create(Person(first: null, last: "رضایی")).DisplayName);
    }

    [Fact]
    public void ACompanyIsShownByItsCompanyNameAndNeedsOne()
    {
        var company = CustomerProfile.Create(new CustomerProfileInput(
            CustomerKind.Legal, null, null, "  مهر  ", "09123456789", null, null, "123", null, null, null, "۱۳۷۰/۰۱/۰۱", null, null));

        Assert.Equal("مهر", company.DisplayName);
        Assert.Equal("123", company.RegistrationNumber);
        Assert.Null(company.BirthDate); // a company has no birth date

        var error = Assert.Single(ErrorsOf(new CustomerProfileInput(
            CustomerKind.Legal, "علی", null, null, "09123456789", null, null, null, null, null, null, null, null, null)));
        Assert.Equal(CustomerField.CompanyName, error.Field);
    }

    [Fact]
    public void ABlankNameIsRejectedWithTheOriginalMessage()
    {
        var exception = Assert.Throws<DomainException>(() => CustomerProfile.Create(Person(first: "  ", last: null)));

        Assert.Equal("نام مشتری را وارد کنید.", exception.Message);
    }

    [Fact]
    public void ANameLongerThanTheDisplayLimitIsRejected()
    {
        var error = Assert.Single(ErrorsOf(Person(first: new string('ا', 121), last: null)));

        Assert.Equal("نام مشتری نمی‌تواند بیشتر از ۱۲۰ نویسه باشد.", error.Message);
    }

    [Fact]
    public void EveryProblemIsReportedAtOnceWithItsField()
    {
        var errors = ErrorsOf(Person(mobile: "0912", nationalId: "1234567890", postal: "123", email: "nope", economic: "12", phone: "1"));

        Assert.Equal(
            [CustomerField.Mobile, CustomerField.NationalId, CustomerField.EconomicCode, CustomerField.PostalCode, CustomerField.Phone, CustomerField.Email],
            errors.Select(error => error.Field).ToList());
    }

    [Fact]
    public void ANationalCodeIsCheckedForAPersonAndALegalIdForACompany()
    {
        Assert.Equal("0499370899", CustomerProfile.Create(Person(nationalId: "۰۴۹۹ ۳۷۰-۸۹۹")).NationalId);

        // a valid 11-digit legal id is not a valid national code, and the other way round
        Assert.Equal(CustomerField.NationalId, Assert.Single(ErrorsOf(Person(nationalId: "10380284790"))).Field);
        var asCompany = new CustomerProfileInput(
            CustomerKind.Legal, null, null, "مهر", "09123456789", "0499370899", null, null, null, null, null, null, null, null);
        Assert.Equal(CustomerField.NationalId, Assert.Single(ErrorsOf(asCompany)).Field);
        Assert.Equal("10380284790", CustomerProfile.Create(asCompany with { NationalId = "10380284790" }).NationalId);
    }

    [Fact]
    public void AForeignNationalsIdIsDigitsOnlyBecauseItsCheckDigitCannotBeVerified()
    {
        var foreign = Person(kind: CustomerKind.Foreign, nationalId: "1234567890123");

        Assert.Equal("1234567890123", CustomerProfile.Create(foreign).NationalId);
        Assert.Single(ErrorsOf(foreign with { NationalId = "12ab" }));
    }

    [Fact]
    public void ALetterInsideANumberIsAnErrorNotSilentlyDropped()
    {
        var error = Assert.Single(ErrorsOf(Person(postal: "12345x7890")));

        Assert.Equal(CustomerField.PostalCode, error.Field);
        Assert.Contains("فقط باید عدد", error.Message);
    }

    [Theory]
    [InlineData("۱۳۷۰/۰۵/۱۲", true)]
    [InlineData("1370-05-12", true)]
    [InlineData("1370/13/01", false)]   // ماه ۱۳ ندارد
    [InlineData("1370/07/31", false)]   // مهر ۳۰ روز است
    [InlineData("1100/01/01", false)]   // سال غیرمعقول
    [InlineData("دیروز", false)]
    public void ABirthDateMustBeARealSolarHijriDate(string text, bool valid)
    {
        var ok = CustomerProfile.TryCreate(Person(birth: text), out var profile, out var errors);

        Assert.Equal(valid, ok);
        if (valid)
        {
            Assert.Equal(1370, profile!.BirthDate!.Value.Year);
        }
        else
        {
            Assert.Equal(CustomerField.BirthDate, Assert.Single(errors).Field);
        }
    }

    [Fact]
    public void OptionalFieldsLeftBlankAreNull()
    {
        var profile = CustomerProfile.Create(Person(nationalId: " ", economic: "", postal: null, phone: "  ", email: ""));

        Assert.Null(profile.NationalId);
        Assert.Null(profile.EconomicCode);
        Assert.Null(profile.PostalCode);
        Assert.Null(profile.Phone);
        Assert.Null(profile.Email);
    }

    [Fact]
    public void TheProfileCanBeTurnedBackIntoFormTextAndCreatedAgainUnchanged()
    {
        var original = CustomerProfile.Create(Person(nationalId: "0499370899", postal: "1234567890", phone: "02112345678", email: "a@b.ir", birth: "۱۳۷۰/۰۵/۱۲"));

        var again = CustomerProfile.Create(original.ToInput());

        Assert.Equal(original.DisplayName, again.DisplayName);
        Assert.Equal(original.NationalId, again.NationalId);
        Assert.Equal(original.BirthDate, again.BirthDate);
        Assert.Equal(original.Email, again.Email);
    }

    [Fact]
    public void ACustomerNumberCanBeAssignedOnlyOnce()
    {
        var customer = Customer.Create(CustomerProfile.Create(Person()));
        Assert.Equal(0, customer.Code);

        customer.AssignCode(7);

        Assert.Equal(7, customer.Code);
        Assert.Throws<DomainException>(() => customer.AssignCode(8));
    }
}
