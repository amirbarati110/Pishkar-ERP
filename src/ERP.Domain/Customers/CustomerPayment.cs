using ERP.Domain.Common;

namespace ERP.Domain.Customers;

/// <summary>
/// How a customer settled part of their account at the counter. Cheques are
/// not offered: a cheque is not money until it clears, and following it to
/// clearing or bouncing is Treasury's job (chapter 10.3).
/// </summary>
public enum CustomerPaymentMethod
{
    Cash = 1,
    Card = 2,
}

/// <summary>
/// «دریافت از مشتری» — money the customer paid toward what they owe. Without
/// this, a نسیه balance could only ever grow, and the banner on the sales screen
/// would keep warning about a debt that was settled long ago. It is the
/// smallest piece of Receivables (chapter 10.1) that keeps that number true;
/// it is recorded once and never edited, like any other posted financial record
/// (Codex rule 15).
/// </summary>
public sealed class CustomerPayment : Entity<Guid>
{
    private const int MaximumNoteLength = 250;

    private CustomerPayment(
        Guid id,
        CustomerId customerId,
        Money amount,
        CustomerPaymentMethod method,
        string? note,
        DateTimeOffset receivedAtUtc)
        : base(id)
    {
        CustomerId = customerId;
        Amount = amount;
        Method = method;
        Note = note;
        ReceivedAtUtc = receivedAtUtc;
    }

    public CustomerId CustomerId { get; }

    public Money Amount { get; }

    public CustomerPaymentMethod Method { get; }

    public string? Note { get; }

    public DateTimeOffset ReceivedAtUtc { get; }

    public static CustomerPayment Receive(
        CustomerId customerId,
        Money amount,
        CustomerPaymentMethod method,
        string? note,
        DateTimeOffset receivedAtUtc)
    {
        if (amount.Rials <= 0)
        {
            throw new DomainException("مبلغ دریافتی باید بیشتر از صفر باشد.");
        }

        if (!Enum.IsDefined(method))
        {
            throw new DomainException("روش دریافت معتبر نیست.");
        }

        var trimmedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (trimmedNote is { Length: > MaximumNoteLength })
        {
            throw new DomainException("توضیح دریافت نمی‌تواند بیشتر از ۲۵۰ نویسه باشد.");
        }

        return new CustomerPayment(Guid.NewGuid(), customerId, amount, method, trimmedNote, receivedAtUtc);
    }
}
