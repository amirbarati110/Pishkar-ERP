using ERP.Domain.Common;

namespace ERP.Domain.Customers;

/// <summary>
/// A counterparty on a sales invoice (طرف حساب). The walk-in "فروش نقدی" case is
/// deliberately <b>not</b> a Customer row: a sale with no customer is the cash
/// sale, which is why <c>Sale.CustomerId</c> is nullable. Everything that needs a
/// named account — نسیه, چک, باشگاه مشتریان, account balance — needs a real
/// Customer.
/// </summary>
public sealed class Customer : Entity<CustomerId>
{
    private const int MaximumNameLength = 120;

    /// <summary>
    /// Beyond this multiple of the credit limit, a manager's approval is no
    /// longer enough and the sale is blocked outright (§6.7). Kept as a named
    /// constant rather than a bare 1.5 in an if-statement.
    /// </summary>
    private const decimal HardBlockMultipleOfCreditLimit = 1.5m;

    private Customer(CustomerId id, string name, string mobile)
        : base(id)
    {
        Name = name;
        Mobile = mobile;
        Status = CustomerStatus.Active;
        CreditLimit = Money.Zero;
        OpeningBalance = Money.Zero;
    }

    public string Name { get; private set; }

    /// <summary>Normalized to Latin digits with no separators, so the duplicate
    /// check in §6.5 can compare on it directly.</summary>
    public string Mobile { get; private set; }

    public string? Address { get; private set; }

    /// <summary>Zero means "no limit set" — not "no credit allowed".</summary>
    public Money CreditLimit { get; private set; }

    /// <summary>Debit carried in when the customer was first entered into the
    /// system (مانده اول دوره).</summary>
    public Money OpeningBalance { get; private set; }

    public CustomerStatus Status { get; private set; }

    /// <summary>Quick create from the POS — name and mobile only (§6.5).</summary>
    public static Customer QuickCreate(string name, string mobile)
    {
        return new Customer(CustomerId.New(), NormalizeName(name), NormalizeMobile(mobile));
    }

    internal static Customer Rehydrate(
        CustomerId id,
        string name,
        string mobile,
        string? address,
        Money creditLimit,
        Money openingBalance,
        CustomerStatus status)
    {
        return new Customer(id, NormalizeName(name), NormalizeMobile(mobile))
        {
            Address = address,
            CreditLimit = creditLimit,
            OpeningBalance = openingBalance,
            Status = status,
        };
    }

    public void UpdateContact(string name, string mobile, string? address)
    {
        Name = NormalizeName(name);
        Mobile = NormalizeMobile(mobile);
        Address = string.IsNullOrWhiteSpace(address) ? null : address.Trim();
    }

    public void SetCreditLimit(Money creditLimit) => CreditLimit = creditLimit;

    public void SetOpeningBalance(Money openingBalance) => OpeningBalance = openingBalance;

    public void Archive() => Status = CustomerStatus.Archived;

    /// <summary>
    /// Decides whether a نسیه/چک sale of <paramref name="newAmount"/> may go
    /// ahead given what the customer already owes (§6.7). The balance is passed
    /// in rather than held on the entity because it is the sum of unsettled
    /// invoices and receipts — a query over other aggregates, not state this one
    /// owns.
    /// </summary>
    public CreditDecision EvaluateCredit(Money currentBalance, Money newAmount)
    {
        if (Status != CustomerStatus.Active)
        {
            return CreditDecision.Blocked;
        }

        if (CreditLimit.Rials == 0)
        {
            return CreditDecision.Allowed;
        }

        var projected = currentBalance.Add(newAmount).Rials;

        if (projected <= CreditLimit.Rials)
        {
            return CreditDecision.Allowed;
        }

        return projected > CreditLimit.Rials * HardBlockMultipleOfCreditLimit
            ? CreditDecision.Blocked
            : CreditDecision.RequiresApproval;
    }

    private static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("نام مشتری را وارد کنید.");
        }

        var normalized = name.Trim();

        if (normalized.Length > MaximumNameLength)
        {
            throw new DomainException("نام مشتری نمی‌تواند بیشتر از ۱۲۰ نویسه باشد.");
        }

        return normalized;
    }

    private static string NormalizeMobile(string mobile)
    {
        var digits = PersianNumber.ToLatinDigitsOnly(mobile);

        if (digits.Length != 11 || !digits.StartsWith("09", StringComparison.Ordinal))
        {
            throw new DomainException("شماره موبایل معتبر نیست؛ نمونه درست: ۰۹۱۲۳۴۵۶۷۸۹");
        }

        return digits;
    }
}
