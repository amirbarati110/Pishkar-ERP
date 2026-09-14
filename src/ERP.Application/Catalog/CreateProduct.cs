using ERP.Application.Audit;
using ERP.Application.Common;
using ERP.Domain.Catalog;
using ERP.Domain.Common;

namespace ERP.Application.Catalog;

public sealed record CreateProductCommand(
    string Name,
    string? Sku,
    CategoryId CategoryId,
    UnitId BaseUnitId,
    Money SalePrice,
    IReadOnlyCollection<string> Barcodes);

public sealed class CreateProductHandler
{
    private readonly IProductRepository _products;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;

    public CreateProductHandler(
        IProductRepository products,
        IAuditWriter audit,
        IUnitOfWork unitOfWork,
        IUserContext userContext,
        IClock clock)
    {
        _products = products;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _userContext = userContext;
        _clock = clock;
    }

    public async Task<Result<ProductId>> ExecuteAsync(
        CreateProductCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        try
        {
            var product = Product.Create(
                command.Name,
                command.Sku,
                command.CategoryId,
                command.BaseUnitId,
                command.SalePrice);

            foreach (var rawBarcode in command.Barcodes)
            {
                var barcode = ProductBarcode.Create(rawBarcode);

                if (await _products.BarcodeExistsAsync(barcode.Value, cancellationToken).ConfigureAwait(false))
                {
                    return Result.Failure<ProductId>(
                        "catalog.product.duplicate-barcode",
                        $"بارکد {barcode.Value} قبلاً برای کالای دیگری ثبت شده است.");
                }

                product.AddBarcode(barcode.Value);
            }

            await _products.AddAsync(product, cancellationToken).ConfigureAwait(false);
            await _audit.WriteAsync(
                new AuditEntry(
                    Guid.NewGuid(),
                    _userContext.UserId,
                    "catalog.product.created",
                    nameof(Product),
                    product.Id.ToString(),
                    null,
                    product.Name,
                    _clock.UtcNow),
                cancellationToken).ConfigureAwait(false);
            await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success(product.Id);
        }
        catch (DomainException exception)
        {
            return Result.Failure<ProductId>("catalog.product.invalid", exception.Message);
        }
        catch (DataConflictException exception)
        {
            return Result.Failure<ProductId>(exception.Code, exception.Message);
        }
    }
}
