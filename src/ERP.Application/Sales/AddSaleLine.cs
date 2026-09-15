using ERP.Application.Catalog;
using ERP.Application.Common;
using ERP.Domain.Catalog;
using ERP.Domain.Common;
using ERP.Domain.Sales;

namespace ERP.Application.Sales;

public sealed record AddSaleLineCommand(SaleId SaleId, ProductId ProductId, decimal Quantity);

public interface IAddSaleLineHandler
{
    Task<Result<bool>> ExecuteAsync(AddSaleLineCommand command, CancellationToken cancellationToken);
}

/// <summary>
/// Adds a product to a Draft sale's cart at the product's current master price —
/// a snapshot taken now, not a live reference, so a later price change never
/// silently alters an already-carted line (source-of-truth §6.11: invoice price
/// vs master price are distinct once on an invoice).
/// </summary>
public sealed class AddSaleLineHandler : IAddSaleLineHandler
{
    private readonly ISaleRepository _sales;
    private readonly IProductRepository _products;
    private readonly IUnitOfWork _unitOfWork;

    public AddSaleLineHandler(
        ISaleRepository sales,
        IProductRepository products,
        IUnitOfWork unitOfWork)
    {
        _sales = sales;
        _products = products;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<bool>> ExecuteAsync(
        AddSaleLineCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var sale = await _sales.GetAsync(command.SaleId, cancellationToken).ConfigureAwait(false);
        if (sale is null)
        {
            return Result.Failure<bool>("sales.sale.not-found", "فاکتور یافت نشد.");
        }

        var product = await _products.GetByIdAsync(command.ProductId, cancellationToken).ConfigureAwait(false);
        if (product is null)
        {
            return Result.Failure<bool>("sales.sale.product-not-found", "کالا یافت نشد.");
        }

        try
        {
            sale.AddOrIncreaseLine(
                command.ProductId,
                Quantity.Create(command.Quantity),
                product.SalePrice);
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
