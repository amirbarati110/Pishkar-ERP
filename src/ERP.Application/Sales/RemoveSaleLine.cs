using ERP.Application.Common;
using ERP.Domain.Catalog;
using ERP.Domain.Common;
using ERP.Domain.Sales;

namespace ERP.Application.Sales;

public sealed record RemoveSaleLineCommand(SaleId SaleId, ProductId ProductId);

public interface IRemoveSaleLineHandler
{
    Task<Result<bool>> ExecuteAsync(RemoveSaleLineCommand command, CancellationToken cancellationToken);
}

public sealed class RemoveSaleLineHandler : IRemoveSaleLineHandler
{
    private readonly ISaleRepository _sales;
    private readonly IUnitOfWork _unitOfWork;

    public RemoveSaleLineHandler(ISaleRepository sales, IUnitOfWork unitOfWork)
    {
        _sales = sales;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<bool>> ExecuteAsync(
        RemoveSaleLineCommand command,
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
            sale.RemoveLine(command.ProductId);
        }
        catch (DomainException exception)
        {
            return Result.Failure<bool>("sales.sale.invalid-line", exception.Message);
        }

        await _sales.SaveAsync(sale, cancellationToken).ConfigureAwait(false);
        await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(true);
    }
}
