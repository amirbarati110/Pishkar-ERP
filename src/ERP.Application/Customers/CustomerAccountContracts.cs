using ERP.Domain.Common;
using ERP.Domain.Customers;
using ERP.Domain.Inventory;

namespace ERP.Application.Customers;

/// <summary>
/// The recorded movements on a customer's account: completed نسیه invoices and
/// payments received. The opening balance lives on <see cref="Customer"/>.
/// </summary>
public sealed record CustomerLedgerEntries(
    IReadOnlyList<CreditInvoice> CreditInvoices,
    IReadOnlyList<Money> Payments);

/// <summary>
/// Reads account movements inside the caller's unit of work, so a credit
/// check made while completing a sale sees the same data the sale commits
/// against.
/// </summary>
public interface ICustomerLedgerReader
{
    Task<CustomerLedgerEntries> ReadAsync(CustomerId customerId, CancellationToken cancellationToken);
}

public interface ICustomerPaymentRepository
{
    Task AddAsync(CustomerPayment payment, CancellationToken cancellationToken);
}

/// <summary>What a cash shift needs from Customers: the cash «دریافت از مشتری» taken at its till.</summary>
public interface ICustomerCashReceiptReader
{
    /// <summary>Cash payments received at <paramref name="warehouseId"/>'s till with <paramref name="fromUtc"/> ≤ received &lt; <paramref name="toUtc"/>.</summary>
    Task<Money> SumCashReceivedAsync(
        WarehouseId warehouseId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken);
}

internal static class CustomerAccounts
{
    public static async Task<CustomerAccount> LoadAsync(
        Customer customer,
        ICustomerLedgerReader ledger,
        CancellationToken cancellationToken)
    {
        var entries = await ledger.ReadAsync(customer.Id, cancellationToken).ConfigureAwait(false);
        return CustomerAccount.Calculate(customer.OpeningBalance, entries.CreditInvoices, entries.Payments);
    }
}
