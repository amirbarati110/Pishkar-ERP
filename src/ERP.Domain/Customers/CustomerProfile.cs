using ERP.Domain.Common;

namespace ERP.Domain.Customers;

/// <summary>
/// Who the customer is in law — it decides which identity number is asked for and checked
/// (checklist «ن-۲»). The buyer's identity is what an electronic tax invoice carries: کد ملی for a
/// real person, شناسه ملی for a company, کد فراگیر for a foreign national.
/// </summary>
public enum CustomerKind
{
    /// <summary>شخص حقیقی — an Iranian person; identity number is the 10-digit کد ملی.</summary>
    Individual = 1,

    /// <summary>شخص حقوقی — a company or organisation; identity number is the 11-digit شناسه ملی.</summary>
    Legal = 2,

    /// <summary>تبعه‌ی خارجی — identity number is the کد فراگیر, which has no check digit we can verify.</summary>
    Foreign = 3,
}

/// <summary>The customer-form fields an error can belong to, so a screen can put the message under the right box.</summary>
public enum CustomerField
{
    FirstName,
    LastName,
    CompanyName,
    Mobile,
    NationalId,
    EconomicCode,
    RegistrationNumber,
    PostalCode,
    Phone,
    Email,
    BirthDate,
    Address,
    Notes,
}

public sealed record CustomerFieldError(CustomerField Field, string Message);

/// <summary>
/// What was typed on the customer form, as text. Nothing here is trusted or normalised yet —
/// that is <see cref="CustomerProfile.TryCreate"/>'s job.
/// </summary>
/// <param name="BirthDate">Solar Hijri, as typed: «۱۳۷۰/۰۵/۱۲».</param>
public sealed record CustomerProfileInput(
    CustomerKind Kind,
    string? FirstName,
    string? LastName,
    string? CompanyName,
    string? Mobile,
    string? NationalId,
    string? EconomicCode,
    string? RegistrationNumber,
    string? PostalCode,
    string? Phone,
    string? Email,
    string? BirthDate,
    string? Address,
    string? Notes);

/// <summary>
/// Everything descriptive about a customer, already checked and in one canonical form (Latin
/// digits, trimmed, empty → null). Built only through <see cref="TryCreate"/>/<see cref="Create"/>,
/// so a stored customer can never hold a mobile, national ID or postal code the rules reject.
///
/// <para>The rules follow the sources checked on 1405/07/01 (checklist «ن-۲»): کد ملی and شناسه
/// ملی carry a check digit and are verified with it; کد پستی is exactly 10 digits; کد اقتصادی is
/// digits only (its length is not asserted — sources differ between the old 12-digit and the
/// newer 14-digit form); کد فراگیر is digits only.</para>
/// </summary>
public sealed class CustomerProfile
{
    private const int MaximumDisplayNameLength = 120;
    private const int MaximumTextLength = 500;

    private CustomerProfile()
    {
    }

    public CustomerKind Kind { get; private init; }

    public string? FirstName { get; private init; }

    public string? LastName { get; private init; }

    public string? CompanyName { get; private init; }

    /// <summary>
    /// The one name every list, search and invoice shows: the company for a legal customer,
    /// otherwise «نام نام‌خانوادگی».
    /// </summary>
    public string DisplayName { get; private init; } = string.Empty;

    /// <summary>Normalized to 11 Latin digits — the key the duplicate check (§6.5) compares on.</summary>
    public string Mobile { get; private init; } = string.Empty;

    /// <summary>کد ملی (10 digits) · شناسه ملی (11) · کد فراگیر — by <see cref="Kind"/>.</summary>
    public string? NationalId { get; private init; }

    public string? EconomicCode { get; private init; }

    public string? RegistrationNumber { get; private init; }

    public string? PostalCode { get; private init; }

    public string? Phone { get; private init; }

    public string? Email { get; private init; }

    public PersianDate? BirthDate { get; private init; }

    public string? Address { get; private init; }

    public string? Notes { get; private init; }

    /// <summary>The minimal profile of the sales screen's «+ مشتری جدید»: a name and a mobile.</summary>
    public static CustomerProfile ForQuickCreate(string name, string mobile) =>
        Create(new CustomerProfileInput(CustomerKind.Individual, name, null, null, mobile, null, null, null, null, null, null, null, null, null));

    public static CustomerProfile Create(CustomerProfileInput input)
    {
        if (TryCreate(input, out var profile, out var errors))
        {
            return profile!;
        }

        throw new DomainException(errors[0].Message);
    }

    /// <summary>Checks every field and reports every problem at once, so a form can mark them all in one go.</summary>
    public static bool TryCreate(
        CustomerProfileInput input,
        out CustomerProfile? profile,
        out IReadOnlyList<CustomerFieldError> errors)
    {
        ArgumentNullException.ThrowIfNull(input);

        var found = new List<CustomerFieldError>();
        var kind = Enum.IsDefined(input.Kind) ? input.Kind : CustomerKind.Individual;

        var firstName = Clean(input.FirstName);
        var lastName = Clean(input.LastName);
        var companyName = Clean(input.CompanyName);

        string displayName;
        if (kind == CustomerKind.Legal)
        {
            displayName = companyName ?? string.Empty;
            if (companyName is null)
            {
                found.Add(new(CustomerField.CompanyName, "نام شرکت را وارد کنید."));
            }
        }
        else
        {
            companyName = null;
            displayName = string.Join(' ', new[] { firstName, lastName }.Where(part => part is not null));
            if (displayName.Length == 0)
            {
                found.Add(new(CustomerField.LastName, "نام مشتری را وارد کنید."));
            }
        }

        if (displayName.Length > MaximumDisplayNameLength)
        {
            found.Add(new(
                kind == CustomerKind.Legal ? CustomerField.CompanyName : CustomerField.LastName,
                "نام مشتری نمی‌تواند بیشتر از ۱۲۰ نویسه باشد."));
        }

        var mobile = PersianNumber.ToLatinDigitsOnly(input.Mobile);
        if (mobile.Length != 11 || !mobile.StartsWith("09", StringComparison.Ordinal))
        {
            found.Add(new(CustomerField.Mobile, "شماره موبایل معتبر نیست؛ نمونه درست: ۰۹۱۲۳۴۵۶۷۸۹"));
        }

        var nationalId = ReadDigits(input.NationalId, CustomerField.NationalId, IdentityLabel(kind), found);
        if (nationalId is not null)
        {
            CheckNationalId(kind, nationalId, found);
        }

        var economicCode = ReadDigits(input.EconomicCode, CustomerField.EconomicCode, "کد اقتصادی", found);
        if (economicCode is { Length: < 10 or > 14 })
        {
            found.Add(new(CustomerField.EconomicCode, "کد اقتصادی باید عدد و بین ۱۰ تا ۱۴ رقم باشد."));
        }

        var registrationNumber = ReadDigits(input.RegistrationNumber, CustomerField.RegistrationNumber, "شماره ثبت", found);
        if (registrationNumber is { Length: > 15 })
        {
            found.Add(new(CustomerField.RegistrationNumber, "شماره ثبت نمی‌تواند بیشتر از ۱۵ رقم باشد."));
        }

        var postalCode = ReadDigits(input.PostalCode, CustomerField.PostalCode, "کد پستی", found);
        if (postalCode is { Length: not 10 })
        {
            found.Add(new(CustomerField.PostalCode, "کد پستی باید دقیقاً ۱۰ رقم باشد."));
        }

        var phone = ReadDigits(input.Phone, CustomerField.Phone, "تلفن", found);
        if (phone is { Length: < 6 or > 11 })
        {
            found.Add(new(CustomerField.Phone, "تلفن را با کد شهر و بدون صفر اضافه وارد کنید؛ نمونه: ۰۲۱۱۲۳۴۵۶۷۸"));
        }

        var email = Clean(input.Email);
        if (email is not null && !LooksLikeEmail(email))
        {
            found.Add(new(CustomerField.Email, "ایمیل معتبر نیست؛ نمونه درست: name@example.com"));
        }

        PersianDate? birthDate = null;
        var birthText = kind == CustomerKind.Legal ? null : Clean(input.BirthDate);
        if (birthText is not null && !TryReadBirthDate(birthText, out birthDate))
        {
            found.Add(new(CustomerField.BirthDate, "تاریخ تولد معتبر نیست؛ نمونه درست: ۱۳۷۰/۰۵/۱۲"));
        }

        var address = Clean(input.Address);
        if (address is { Length: > MaximumTextLength })
        {
            found.Add(new(CustomerField.Address, "نشانی نمی‌تواند بیشتر از ۵۰۰ نویسه باشد."));
        }

        var notes = Clean(input.Notes);
        if (notes is { Length: > MaximumTextLength })
        {
            found.Add(new(CustomerField.Notes, "توضیحات نمی‌تواند بیشتر از ۵۰۰ نویسه باشد."));
        }

        errors = found;
        if (found.Count > 0)
        {
            profile = null;
            return false;
        }

        profile = new CustomerProfile
        {
            Kind = kind,
            FirstName = firstName,
            LastName = lastName,
            CompanyName = companyName,
            DisplayName = displayName,
            Mobile = mobile,
            NationalId = nationalId,
            EconomicCode = economicCode,
            RegistrationNumber = registrationNumber,
            PostalCode = postalCode,
            Phone = phone,
            Email = email,
            BirthDate = birthDate,
            Address = address,
            Notes = notes,
        };
        return true;
    }

    /// <summary>The same fields as text again, for opening the edit form on an existing customer.</summary>
    public CustomerProfileInput ToInput() =>
        new(
            Kind,
            FirstName,
            LastName,
            CompanyName,
            Mobile,
            NationalId,
            EconomicCode,
            RegistrationNumber,
            PostalCode,
            Phone,
            Email,
            BirthDate?.ToString(),
            Address,
            Notes);

    /// <summary>Rebuilds a profile from stored columns without judging it: rows written before a rule existed must still load.</summary>
    internal static CustomerProfile Rehydrate(
        CustomerKind kind,
        string displayName,
        string? firstName,
        string? lastName,
        string? companyName,
        string mobile,
        string? nationalId,
        string? economicCode,
        string? registrationNumber,
        string? postalCode,
        string? phone,
        string? email,
        PersianDate? birthDate,
        string? address,
        string? notes) =>
        new()
        {
            Kind = kind,
            FirstName = firstName,
            LastName = lastName,
            CompanyName = companyName,
            DisplayName = displayName,
            Mobile = mobile,
            NationalId = nationalId,
            EconomicCode = economicCode,
            RegistrationNumber = registrationNumber,
            PostalCode = postalCode,
            Phone = phone,
            Email = email,
            BirthDate = birthDate,
            Address = address,
            Notes = notes,
        };

    private static string IdentityLabel(CustomerKind kind) => kind switch
    {
        CustomerKind.Legal => "شناسه ملی",
        CustomerKind.Foreign => "کد فراگیر",
        _ => "کد ملی",
    };

    private static void CheckNationalId(CustomerKind kind, string digits, List<CustomerFieldError> errors)
    {
        switch (kind)
        {
            case CustomerKind.Individual when !IranianIdentifiers.IsValidNationalCode(digits):
                errors.Add(new(CustomerField.NationalId, "کد ملی معتبر نیست؛ باید ۱۰ رقم باشد و رقم آخرش درست باشد."));
                break;
            case CustomerKind.Legal when !IranianIdentifiers.IsValidLegalId(digits):
                errors.Add(new(CustomerField.NationalId, "شناسه ملی معتبر نیست؛ باید ۱۱ رقم باشد و رقم آخرش درست باشد."));
                break;
            case CustomerKind.Foreign when digits.Length is < 5 or > 15:
                errors.Add(new(CustomerField.NationalId, "کد فراگیر باید عدد باشد (بین ۵ تا ۱۵ رقم)."));
                break;
        }
    }

    /// <summary>Trims; blank becomes null. Letters and symbols are kept — they are checked by the field's own rule.</summary>
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Reads a number typed in Persian or Latin digits, ignoring spaces and dashes. Anything else
    /// in it (a letter, a dot) is an error — silently dropping it would store a different number
    /// from the one the operator typed.
    /// </summary>
    private static string? ReadDigits(string? raw, CustomerField field, string label, List<CustomerFieldError> errors)
    {
        var text = Clean(raw);
        if (text is null)
        {
            return null;
        }

        var digits = PersianNumber.ToLatinDigitsOnly(text);
        var extras = text.Count(character => !char.IsWhiteSpace(character) && character != '-');
        if (digits.Length != extras)
        {
            errors.Add(new(field, $"{label} فقط باید عدد باشد."));
            return null;
        }

        return digits.Length == 0 ? null : digits;
    }

    private static bool LooksLikeEmail(string email)
    {
        if (email.Length > 100 || email.Any(char.IsWhiteSpace))
        {
            return false;
        }

        var at = email.IndexOf('@', StringComparison.Ordinal);
        if (at < 1 || at != email.LastIndexOf('@'))
        {
            return false;
        }

        var domain = email[(at + 1)..];
        var dot = domain.LastIndexOf('.');
        return dot > 0 && dot < domain.Length - 1;
    }

    private static bool TryReadBirthDate(string text, out PersianDate? date)
    {
        date = null;
        var parts = PersianNumber.DigitsToLatin(text).Split('/', '-', '.');
        if (parts.Length != 3
            || !int.TryParse(parts[0], out var year)
            || !int.TryParse(parts[1], out var month)
            || !int.TryParse(parts[2], out var day)
            || year is < 1200 or > 1500)
        {
            return false;
        }

        try
        {
            date = PersianDate.Create(year, month, day);
            return true;
        }
        catch (DomainException)
        {
            return false;
        }
    }
}
