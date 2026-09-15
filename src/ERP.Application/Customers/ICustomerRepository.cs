using ERP.Domain.Customers;

namespace ERP.Application.Customers;

public interface ICustomerRepository
{
    Task<Customer?> GetByIdAsync(CustomerId customerId, CancellationToken cancellationToken);

    /// <param name="normalizedMobile">Already in <see cref="Customer.Mobile"/> form (11 Latin digits).</param>
    Task<Customer?> FindByMobileAsync(string normalizedMobile, CancellationToken cancellationToken);

    Task AddAsync(Customer customer, CancellationToken cancellationToken);
}
