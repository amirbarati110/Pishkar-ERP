using ERP.Application.Common;
using ERP.Application.Customers;
using ERP.Domain.Common;
using ERP.Domain.Customers;
using ERP.Domain.Sales;

namespace ERP.Application.Sales;

/// <param name="CustomerId">Null switches the invoice back to «مشتری نقدی».</param>
public sealed record SetSaleCustomerCommand(SaleId SaleId, CustomerId? CustomerId);

public interface ISetSaleCustomerHandler
{
    Task<Result<bool>> ExecuteAsync(SetSaleCustomerCommand command, CancellationToken cancellationToken);
}

/// <summary>
/// «انتخاب مشتری» (F4) on an open invoice. Refuses a customer that does not
/// exist or is archived, so an invoice can never point at someone the
/// customer search would not have offered.
/// </summary>
public sealed class SetSaleCustomerHandler : ISetSaleCustomerHandler
{
    private readonly ISaleRepository _sales;
    private readonly ICustomerRepository _customers;
    private readonly IUnitOfWork _unitOfWork;

    public SetSaleCustomerHandler(ISaleRepository sales, ICustomerRepository customers, IUnitOfWork unitOfWork)
    {
        _sales = sales;
        _customers = customers;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<bool>> ExecuteAsync(SetSaleCustomerCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var sale = await _sales.GetAsync(command.SaleId, cancellationToken).ConfigureAwait(false);
        if (sale is null)
        {
            return Result.Failure<bool>("sales.sale.not-found", "فاکتور یافت نشد.");
        }

        if (command.CustomerId is { } customerId)
        {
            var customer = await _customers.GetByIdAsync(customerId, cancellationToken).ConfigureAwait(false);
            if (customer is null)
            {
                return Result.Failure<bool>("customers.customer.not-found", "مشتری یافت نشد.");
            }

            if (customer.Status != CustomerStatus.Active)
            {
                return Result.Failure<bool>(
                    "customers.customer.archived",
                    $"«{customer.Name}» بایگانی شده است و نمی‌توان برایش فاکتور صادر کرد.");
            }
        }

        try
        {
            sale.AssignCustomer(command.CustomerId);
        }
        catch (DomainException exception)
        {
            return Result.Failure<bool>("sales.sale.invalid", exception.Message);
        }

        await _sales.SaveAsync(sale, cancellationToken).ConfigureAwait(false);
        await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(true);
    }
}
