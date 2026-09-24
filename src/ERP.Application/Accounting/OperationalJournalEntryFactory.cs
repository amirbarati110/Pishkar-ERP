using ERP.Domain.Accounting;
using ERP.Domain.Common;
using ERP.Domain.Customers;

namespace ERP.Application.Accounting;

/// <summary>
/// «سند حسابداری حداقلی» for the operations that are not a sale or a return but still move money
/// or stock — §10.5 Auto Accounting: produced by the operation itself, never typed by hand. Each
/// returns null when there is nothing to post (a zero amount), since an entry of zeros says nothing.
/// </summary>
public static class OperationalJournalEntryFactory
{
    /// <summary>«دریافت از مشتری»: the money arrives (cash or card terminal) and the customer owes that much less.</summary>
    public static JournalEntry CustomerPayment(Guid paymentId, Money amount, CustomerPaymentMethod method, DateTimeOffset postedAtUtc)
    {
        var receivedInto = method switch
        {
            CustomerPaymentMethod.Cash => AccountCode.Cash,
            CustomerPaymentMethod.Card => AccountCode.CardClearing,
            _ => throw new DomainException("این روش دریافت برای سند حسابداری پشتیبانی نمی‌شود."),
        };

        return JournalEntry.Create(
            JournalSourceType.CustomerPayment,
            paymentId.ToString("D"),
            postedAtUtc,
            [JournalLine.Debited(receivedInto, amount), JournalLine.Credited(AccountCode.AccountsReceivable, amount)]);
    }

    /// <summary>
    /// «موجودی اول دوره»: stock the business already owned enters the books at its cost, against
    /// opening equity. <paramref name="layerId"/> is the inventory layer the receipt created.
    /// </summary>
    public static JournalEntry? OpeningStock(string layerId, Money value, DateTimeOffset postedAtUtc) =>
        value.Rials == 0
            ? null
            : JournalEntry.Create(
                JournalSourceType.OpeningStock,
                layerId,
                postedAtUtc,
                [JournalLine.Debited(AccountCode.Inventory, value), JournalLine.Credited(AccountCode.OpeningBalanceEquity, value)]);

    /// <summary>A customer's debt carried in from the old books, against opening equity.</summary>
    public static JournalEntry? CustomerOpeningBalance(CustomerId customerId, Money balance, DateTimeOffset postedAtUtc) =>
        balance.Rials == 0
            ? null
            : JournalEntry.Create(
                JournalSourceType.CustomerOpeningBalance,
                customerId.ToString(),
                postedAtUtc,
                [JournalLine.Debited(AccountCode.AccountsReceivable, balance), JournalLine.Credited(AccountCode.OpeningBalanceEquity, balance)]);
}
