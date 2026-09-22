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
    /// <summary>
    /// Beyond this multiple of the credit limit, a manager's approval is no
    /// longer enough and the sale is blocked outright (§6.7). Kept as a named
    /// constant rather than a bare 1.5 in an if-statement.
    /// </summary>
    private const decimal HardBlockMultipleOfCreditLimit = 1.5m;

    private Customer(CustomerId id, CustomerProfile profile)
        : base(id)
    {
        Profile = profile;
        Status = CustomerStatus.Active;
        CreditLimit = Money.Zero;
        OpeningBalance = Money.Zero;
    }

    /// <summary>Everything descriptive — kind, names, identity numbers, contact details (checklist «ن-۲»).</summary>
    public CustomerProfile Profile { get; private set; }

    /// <summary>The name every list, search and invoice shows (company name for a legal customer).</summary>
    public string Name => Profile.DisplayName;

    /// <summary>Normalized to Latin digits with no separators, so the duplicate
    /// check in §6.5 can compare on it directly.</summary>
    public string Mobile => Profile.Mobile;

    public string? Address => Profile.Address;

    /// <summary>
    /// The customer number (شماره اشتراک) shown in lists and searched at the till. Given by the
    /// database sequence when the customer is first stored; zero until then.
    /// </summary>
    public long Code { get; private set; }

    /// <summary>Zero means "no limit set" — not "no credit allowed".</summary>
    public Money CreditLimit { get; private set; }

    /// <summary>Debit carried in when the customer was first entered into the
    /// system (مانده اول دوره).</summary>
    public Money OpeningBalance { get; private set; }

    public CustomerStatus Status { get; private set; }

    /// <summary>Quick create from the POS — name and mobile only (§6.5).</summary>
    public static Customer QuickCreate(string name, string mobile)
    {
        return new Customer(CustomerId.New(), CustomerProfile.ForQuickCreate(name, mobile));
    }

    /// <summary>The full customer form of «لیست مشتریان».</summary>
    public static Customer Create(CustomerProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return new Customer(CustomerId.New(), profile);
    }

    internal static Customer Rehydrate(
        CustomerId id,
        long code,
        CustomerProfile profile,
        Money creditLimit,
        Money openingBalance,
        CustomerStatus status)
    {
        return new Customer(id, profile)
        {
            Code = code,
            CreditLimit = creditLimit,
            OpeningBalance = openingBalance,
            Status = status,
        };
    }

    /// <summary>Called once, by the store, with the next number of its sequence.</summary>
    public void AssignCode(long code)
    {
        if (Code != 0)
        {
            throw new DomainException("این مشتری قبلاً شماره اشتراک گرفته است.");
        }

        Code = code;
    }

    public void UpdateProfile(CustomerProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Profile = profile;
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
}
