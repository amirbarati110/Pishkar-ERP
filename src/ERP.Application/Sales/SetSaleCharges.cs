using ERP.Application.Common;
using ERP.Domain.Common;
using ERP.Domain.Sales;

namespace ERP.Application.Sales;

/// <summary>
/// The invoice footer the cashier types into: تخفیف and خدمات/هزینه.
/// Both are amounts — a percentage typed on screen is converted to an amount
/// before it gets here, because the ledger stores money, not ratios.
/// The tax rate is not set here: it is applied at completion (§10.10).
/// </summary>
public sealed record SetSaleChargesCommand(
    SaleId SaleId,
    long DiscountRials,
    long ServiceChargeRials);

public interface ISetSaleChargesHandler
{
    Task<Result<bool>> ExecuteAsync(SetSaleChargesCommand command, CancellationToken cancellationToken);
}

public sealed class SetSaleChargesHandler : ISetSaleChargesHandler
{
    private readonly ISaleRepository _sales;
    private readonly IUnitOfWork _unitOfWork;

    public SetSaleChargesHandler(ISaleRepository sales, IUnitOfWork unitOfWork)
    {
        _sales = sales;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<bool>> ExecuteAsync(
        SetSaleChargesCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var sale = await _sales.GetAsync(command.SaleId, cancellationToken).ConfigureAwait(false);
        if (sale is null)
        {
            return Result.Failure<bool>("sales.sale.not-found", "فاکتور یافت نشد.");
        }

        try
        {
            sale.ApplyDiscount(Money.FromRials(command.DiscountRials));
            sale.ApplyServiceCharge(Money.FromRials(command.ServiceChargeRials));
        }
        catch (DomainException exception)
        {
            return Result.Failure<bool>("sales.sale.invalid-charges", exception.Message);
        }

        await _sales.SaveAsync(sale, cancellationToken).ConfigureAwait(false);
        await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(true);
    }
}
