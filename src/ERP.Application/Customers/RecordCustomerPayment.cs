using System.Globalization;
using ERP.Application.Audit;
using ERP.Application.Common;
using ERP.Domain.Common;
using ERP.Domain.Customers;

namespace ERP.Application.Customers;

public sealed record RecordCustomerPaymentCommand(
    CustomerId CustomerId,
    long AmountRials,
    CustomerPaymentMethod Method,
    string? Note);

public interface IRecordCustomerPaymentHandler
{
    Task<Result<Guid>> ExecuteAsync(RecordCustomerPaymentCommand command, CancellationToken cancellationToken);
}

/// <summary>
/// Records «دریافت از مشتری». Paying more than is owed is accepted and shows
/// as an advance (بستانکار) — customers do pay ahead — so there is no upper
/// bound check. An archived customer can still settle an old debt.
/// </summary>
public sealed class RecordCustomerPaymentHandler : IRecordCustomerPaymentHandler
{
    private readonly ICustomerRepository _customers;
    private readonly ICustomerPaymentRepository _payments;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;

    public RecordCustomerPaymentHandler(
        ICustomerRepository customers,
        ICustomerPaymentRepository payments,
        IAuditWriter audit,
        IUnitOfWork unitOfWork,
        IUserContext userContext,
        IClock clock)
    {
        _customers = customers;
        _payments = payments;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _userContext = userContext;
        _clock = clock;
    }

    public async Task<Result<Guid>> ExecuteAsync(
        RecordCustomerPaymentCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var customer = await _customers.GetByIdAsync(command.CustomerId, cancellationToken).ConfigureAwait(false);
        if (customer is null)
        {
            return Result.Failure<Guid>("customers.customer.not-found", "مشتری یافت نشد.");
        }

        try
        {
            var payment = CustomerPayment.Receive(
                customer.Id,
                Money.FromRials(command.AmountRials),
                command.Method,
                command.Note,
                _clock.UtcNow);

            await _payments.AddAsync(payment, cancellationToken).ConfigureAwait(false);
            await _audit.WriteAsync(
                new AuditEntry(
                    Guid.NewGuid(),
                    _userContext.UserId,
                    "customers.payment.received",
                    nameof(CustomerPayment),
                    payment.Id.ToString("D"),
                    null,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"customer={customer.Id};amount={payment.Amount.Rials};method={payment.Method}"),
                    _clock.UtcNow),
                cancellationToken).ConfigureAwait(false);
            await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success(payment.Id);
        }
        catch (DomainException exception)
        {
            return Result.Failure<Guid>("customers.payment.invalid", exception.Message);
        }
    }
}
