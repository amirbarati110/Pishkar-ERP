using ERP.Domain.Common;

namespace ERP.Domain.Accounting;

/// <summary>One side of a double-entry line — exactly one of Debit/Credit is non-zero, enforced by only offering the two factories below.</summary>
public sealed record JournalLine
{
    private JournalLine(AccountCode account, Money debit, Money credit)
    {
        Account = account;
        Debit = debit;
        Credit = credit;
    }

    public AccountCode Account { get; }

    public Money Debit { get; }

    public Money Credit { get; }

    public static JournalLine Debited(AccountCode account, Money amount)
    {
        if (amount.Rials == 0)
        {
            throw new DomainException("سطر سند حسابداری نمی‌تواند مبلغ صفر داشته باشد.");
        }

        return new JournalLine(account, amount, Money.Zero);
    }

    public static JournalLine Credited(AccountCode account, Money amount)
    {
        if (amount.Rials == 0)
        {
            throw new DomainException("سطر سند حسابداری نمی‌تواند مبلغ صفر داشته باشد.");
        }

        return new JournalLine(account, Money.Zero, amount);
    }
}
