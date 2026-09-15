using ERP.Application.Common;
using ERP.Domain.Inventory;
using ERP.Domain.Sales;

namespace ERP.Application.Sales;

/// <summary>
/// Every sale starts as the walk-in cash sale; a customer is chosen afterwards
/// through <see cref="SetSaleCustomerCommand"/>, the one path that checks the
/// customer exists and is active.
/// </summary>
public sealed record StartSaleCommand(WarehouseId WarehouseId);

public interface IStartSaleHandler
{
    Task<Result<SaleId>> ExecuteAsync(StartSaleCommand command, CancellationToken cancellationToken);
}

/// <summary>
/// Opens a new Draft sale (a fresh POS cart). Not audited: an opened-but-abandoned
/// cart is not a business event worth an audit row (source-of-truth Definition of
/// Done #6 says "audit where needed" — completion and cancellation are the
/// meaningful events, handled by <see cref="CompleteSaleHandler"/>).
/// </summary>
public sealed class StartSaleHandler : IStartSaleHandler
{
    private readonly ISaleRepository _sales;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public StartSaleHandler(ISaleRepository sales, IUnitOfWork unitOfWork, IClock clock)
    {
        _sales = sales;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<SaleId>> ExecuteAsync(
        StartSaleCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var sale = Sale.OpenDraft(command.WarehouseId, customerId: null, _clock.UtcNow);
        await _sales.SaveAsync(sale, cancellationToken).ConfigureAwait(false);
        await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(sale.Id);
    }
}
