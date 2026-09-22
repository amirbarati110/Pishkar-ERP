using ERP.Domain.Customers;

namespace ERP.Application.Customers;

public interface ICustomerRepository
{
    Task<Customer?> GetByIdAsync(CustomerId customerId, CancellationToken cancellationToken);

    /// <param name="normalizedMobile">Already in <see cref="Customer.Mobile"/> form (11 Latin digits).</param>
    Task<Customer?> FindByMobileAsync(string normalizedMobile, CancellationToken cancellationToken);

    /// <param name="normalizedNationalId">Digits only, as <see cref="CustomerProfile.NationalId"/> holds it.</param>
    Task<Customer?> FindByNationalIdAsync(string normalizedNationalId, CancellationToken cancellationToken);

    Task AddAsync(Customer customer, CancellationToken cancellationToken);

    /// <summary>Writes the contact details, credit limit and status of an existing customer.</summary>
    Task UpdateAsync(Customer customer, CancellationToken cancellationToken);
}
