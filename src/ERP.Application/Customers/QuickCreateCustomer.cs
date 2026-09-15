using ERP.Application.Audit;
using ERP.Application.Common;
using ERP.Domain.Common;
using ERP.Domain.Customers;

namespace ERP.Application.Customers;

public sealed record QuickCreateCustomerCommand(string Name, string Mobile);

public interface IQuickCreateCustomerHandler
{
    Task<Result<CustomerId>> ExecuteAsync(QuickCreateCustomerCommand command, CancellationToken cancellationToken);
}

/// <summary>
/// «+ مشتری جدید» on the sales screen — name and mobile only, with the
/// duplicate check of source-of-truth §6.5 before anything is written.
///
/// <para>The mobile number is treated as the customer's identity and must be
/// unique. Two records for one person silently split their balance, purchase
/// history and loyalty points (chapter 7 keys all of these off the customer),
/// and the cashier has no way of noticing — a far worse failure than asking a
/// second family member to be entered under their own number. The check runs
/// on the normalized number, so «۰۹۱۲ ۳۴۵ ۶۷۸۹» and «09123456789» collide as
/// they should.</para>
/// </summary>
public sealed class QuickCreateCustomerHandler : IQuickCreateCustomerHandler
{
    private readonly ICustomerRepository _customers;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;

    public QuickCreateCustomerHandler(
        ICustomerRepository customers,
        IAuditWriter audit,
        IUnitOfWork unitOfWork,
        IUserContext userContext,
        IClock clock)
    {
        _customers = customers;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _userContext = userContext;
        _clock = clock;
    }

    public async Task<Result<CustomerId>> ExecuteAsync(
        QuickCreateCustomerCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        try
        {
            var customer = Customer.QuickCreate(command.Name, command.Mobile);

            var existing = await _customers
                .FindByMobileAsync(customer.Mobile, cancellationToken)
                .ConfigureAwait(false);
            if (existing is not null)
            {
                return Result.Failure<CustomerId>(
                    "customers.customer.duplicate-mobile",
                    $"این شماره موبایل قبلاً برای «{existing.Name}» ثبت شده است؛ همان مشتری را انتخاب کنید.");
            }

            await _customers.AddAsync(customer, cancellationToken).ConfigureAwait(false);
            await _audit.WriteAsync(
                new AuditEntry(
                    Guid.NewGuid(),
                    _userContext.UserId,
                    "customers.customer.created",
                    nameof(Customer),
                    customer.Id.ToString(),
                    null,
                    customer.Name,
                    _clock.UtcNow),
                cancellationToken).ConfigureAwait(false);
            await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success(customer.Id);
        }
        catch (DomainException exception)
        {
            return Result.Failure<CustomerId>("customers.customer.invalid", exception.Message);
        }
        catch (DataConflictException exception)
        {
            // Another till registered the same number between our check and our
            // insert; the database's unique constraint is the final word.
            return Result.Failure<CustomerId>(exception.Code, exception.Message);
        }
    }
}
