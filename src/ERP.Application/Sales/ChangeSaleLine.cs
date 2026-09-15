using ERP.Application.Catalog;
using ERP.Application.Common;
using ERP.Domain.Catalog;
using ERP.Domain.Common;
using ERP.Domain.Sales;

namespace ERP.Application.Sales;

/// <summary>
/// The "مداد" (edit row) action on the invoice: quantity, the price charged on
/// this invoice, and the row discount — source-of-truth §6.10.
/// </summary>
/// <param name="UpdateCatalogPrice">
/// §6.11: changing the price on an invoice does not change the product's master
/// price. Only when the cashier explicitly ticks "قیمت اصلی کالا هم به‌روز شود"
/// does the catalogue price change too — and that is an inventory-wide effect,
/// so it is a separate, deliberate flag rather than a side effect of editing a row.
/// </param>
public sealed record ChangeSaleLineCommand(
    SaleId SaleId,
    ProductId ProductId,
    decimal Quantity,
    long UnitPriceRials,
    long DiscountRials,
    bool UpdateCatalogPrice = false);

public interface IChangeSaleLineHandler
{
    Task<Result<bool>> ExecuteAsync(ChangeSaleLineCommand command, CancellationToken cancellationToken);
}

public sealed class ChangeSaleLineHandler : IChangeSaleLineHandler
{
    private readonly ISaleRepository _sales;
    private readonly IProductRepository _products;
    private readonly IUnitOfWork _unitOfWork;

    public ChangeSaleLineHandler(
        ISaleRepository sales,
        IProductRepository products,
        IUnitOfWork unitOfWork)
    {
        _sales = sales;
        _products = products;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<bool>> ExecuteAsync(
        ChangeSaleLineCommand command,
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
            sale.ChangeLine(
                command.ProductId,
                Quantity.Create(command.Quantity),
                Money.FromRials(command.UnitPriceRials),
                Money.FromRials(command.DiscountRials));

            if (command.UpdateCatalogPrice)
            {
                var product = await _products.GetByIdAsync(command.ProductId, cancellationToken).ConfigureAwait(false);
                if (product is null)
                {
                    return Result.Failure<bool>("sales.sale.product-not-found", "کالا یافت نشد.");
                }

                product.ChangePrice(Money.FromRials(command.UnitPriceRials));
                await _products.UpdateAsync(product, cancellationToken).ConfigureAwait(false);
            }
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
