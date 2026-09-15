using ERP.Application.Audit;
using ERP.Application.Common;
using ERP.Domain.Common;
using ERP.Domain.Sales;

namespace ERP.Application.Sales;

public sealed record CancelSaleCommand(SaleId SaleId);

public interface ICancelSaleHandler
{
    Task<Result<bool>> ExecuteAsync(CancelSaleCommand command, CancellationToken cancellationToken);
}

/// <summary>
/// «حذف» on an open invoice: the draft is marked Cancelled, not deleted, so
/// the audit trail keeps what was in the cart. A completed invoice cannot be
/// cancelled this way — reversing a posted sale is a return (§6.25).
/// </summary>
public sealed class CancelSaleHandler : ICancelSaleHandler
{
    private readonly ISaleRepository _sales;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;

    public CancelSaleHandler(
        ISaleRepository sales,
        IAuditWriter audit,
        IUnitOfWork unitOfWork,
        IUserContext userContext,
        IClock clock)
    {
        _sales = sales;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _userContext = userContext;
        _clock = clock;
    }

    public async Task<Result<bool>> ExecuteAsync(CancelSaleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var sale = await _sales.GetAsync(command.SaleId, cancellationToken).ConfigureAwait(false);
        if (sale is null)
        {
            return Result.Failure<bool>("sales.sale.not-found", "فاکتور یافت نشد.");
        }

        try
        {
            sale.Cancel();
        }
        catch (DomainException)
        {
            return Result.Failure<bool>(
                "sales.sale.not-draft",
                "این فاکتور ثبت نهایی شده و حذف نمی‌شود؛ برای برگرداندن آن از «مرجوعی» استفاده کنید.");
        }

        await _sales.SaveAsync(sale, cancellationToken).ConfigureAwait(false);
        await _audit.WriteAsync(
            new AuditEntry(
                Guid.NewGuid(),
                _userContext.UserId,
                "sales.sale.cancelled",
                nameof(Sale),
                sale.Id.ToString(),
                null,
                string.Join(';', sale.Lines.Select(line => string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"{line.ProductId}x{line.Quantity.Value}"))),
                _clock.UtcNow),
            cancellationToken).ConfigureAwait(false);
        await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(true);
    }
}
