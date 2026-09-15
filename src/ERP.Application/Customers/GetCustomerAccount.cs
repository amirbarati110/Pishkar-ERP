using ERP.Application.Common;
using ERP.Domain.Common;
using ERP.Domain.Customers;

namespace ERP.Application.Customers;

public sealed record GetCustomerAccountQuery(CustomerId CustomerId);

/// <summary>
/// Everything the customer banner on the sales screen needs (§6.6), already
/// computed: the screen formats, it does not add up.
/// </summary>
/// <param name="CreditLimit">Zero means no limit has been set.</param>
/// <param name="OpenInvoiceNumbers">Oldest first.</param>
public sealed record CustomerAccountSummary(
    CustomerId CustomerId,
    string Name,
    string Mobile,
    CustomerStatus Status,
    Money CreditLimit,
    Money Debt,
    Money Advance,
    IReadOnlyList<long> OpenInvoiceNumbers);

public interface IGetCustomerAccountHandler
{
    Task<Result<CustomerAccountSummary>> ExecuteAsync(GetCustomerAccountQuery query, CancellationToken cancellationToken);
}

public sealed class GetCustomerAccountHandler : IGetCustomerAccountHandler
{
    private readonly ICustomerRepository _customers;
    private readonly ICustomerLedgerReader _ledger;

    public GetCustomerAccountHandler(ICustomerRepository customers, ICustomerLedgerReader ledger)
    {
        _customers = customers;
        _ledger = ledger;
    }

    public async Task<Result<CustomerAccountSummary>> ExecuteAsync(
        GetCustomerAccountQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var customer = await _customers.GetByIdAsync(query.CustomerId, cancellationToken).ConfigureAwait(false);
        if (customer is null)
        {
            return Result.Failure<CustomerAccountSummary>("customers.customer.not-found", "مشتری یافت نشد.");
        }

        var account = await CustomerAccounts.LoadAsync(customer, _ledger, cancellationToken).ConfigureAwait(false);

        return Result.Success(new CustomerAccountSummary(
            customer.Id,
            customer.Name,
            customer.Mobile,
            customer.Status,
            customer.CreditLimit,
            account.Debt,
            account.Advance,
            account.OpenInvoices.Select(invoice => invoice.Number).ToList()));
    }
}
