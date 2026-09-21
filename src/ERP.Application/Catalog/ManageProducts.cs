using ERP.Application.Audit;
using ERP.Application.Common;
using ERP.Domain.Catalog;
using ERP.Domain.Common;

namespace ERP.Application.Catalog;

// «لیست کالاها» (checklist «م», step 2): reading the list, editing a product, and taking
// it out of use. «حذف» on the screen is the archive below — a product that was ever sold
// must stay in the books (rule 15 / §3.10), so it disappears from lists and from selling
// but its history is kept.

/// <summary>
/// What the list screen asks for. <see cref="Term"/> matches half words in the name, the
/// code or a barcode; <see cref="CategoryId"/> also includes every sub-category under it.
/// </summary>
public sealed record ProductListQuery(string? Term, CategoryId? CategoryId, int Take = ListProductsHandler.DefaultTake);

/// <summary>One row — carries everything the edit form needs, so opening an edit costs no second read.</summary>
public sealed record ProductListRow(
    ProductId Id,
    string? Sku,
    string Name,
    CategoryId CategoryId,
    string CategoryName,
    UnitId BaseUnitId,
    string UnitSymbol,
    Money SalePrice,
    decimal Stock,
    string? PrimaryBarcode);

/// <summary>
/// <see cref="Rows"/> is at most the asked-for count; <see cref="TotalCount"/> and
/// <see cref="TotalStock"/> cover every match, so the footer can say «۳۶۵ کالا» even when
/// fewer rows are drawn.
/// </summary>
public sealed record ProductListPage(IReadOnlyList<ProductListRow> Rows, int TotalCount, decimal TotalStock);

public interface IProductListReader
{
    Task<ProductListPage> ListAsync(ProductListQuery query, CancellationToken cancellationToken);
}

public interface IListProductsHandler
{
    Task<ProductListPage> ExecuteAsync(ProductListQuery query, CancellationToken cancellationToken);
}

public sealed class ListProductsHandler : IListProductsHandler
{
    public const int DefaultTake = 300;
    public const int MaximumTake = 500;

    private readonly IProductListReader _reader;

    public ListProductsHandler(IProductListReader reader)
    {
        _reader = reader;
    }

    public Task<ProductListPage> ExecuteAsync(ProductListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var term = string.IsNullOrWhiteSpace(query.Term) ? null : query.Term.Trim();
        var take = Math.Clamp(query.Take, 1, MaximumTake);
        return _reader.ListAsync(query with { Term = term, Take = take }, cancellationToken);
    }
}

public sealed record UpdateProductCommand(
    ProductId ProductId,
    string Name,
    string? Sku,
    CategoryId CategoryId,
    Money SalePrice);

public interface IUpdateProductHandler
{
    Task<Result<bool>> ExecuteAsync(UpdateProductCommand command, CancellationToken cancellationToken);
}

public sealed class UpdateProductHandler : IUpdateProductHandler
{
    private readonly IProductRepository _products;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;

    public UpdateProductHandler(
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

    public async Task<Result<bool>> ExecuteAsync(UpdateProductCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        try
        {
            var product = await _products.GetByIdAsync(command.ProductId, cancellationToken).ConfigureAwait(false);
            if (product is null)
            {
                return Result.Failure<bool>("catalog.product.not-found", "این کالا پیدا نشد؛ ممکن است حذف شده باشد.");
            }

            if (product.Status == ProductStatus.Archived)
            {
                return Result.Failure<bool>("catalog.product.archived", "این کالا حذف شده و دیگر قابل ویرایش نیست.");
            }

            var before = Describe(product);
            product.Update(command.Name, command.Sku, command.CategoryId, command.SalePrice);

            await _products.UpdateAsync(product, cancellationToken).ConfigureAwait(false);
            await _audit.WriteAsync(
                new AuditEntry(
                    Guid.NewGuid(),
                    _userContext.UserId,
                    "catalog.product.updated",
                    nameof(Product),
                    product.Id.ToString(),
                    before,
                    Describe(product),
                    _clock.UtcNow),
                cancellationToken).ConfigureAwait(false);
            await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success(true);
        }
        catch (DomainException exception)
        {
            return Result.Failure<bool>("catalog.product.invalid", exception.Message);
        }
        catch (DataConflictException exception)
        {
            return Result.Failure<bool>(exception.Code, exception.Message);
        }
    }

    private static string Describe(Product product) =>
        $"{product.Name} | {product.Sku} | {product.SalePrice.Rials} | {product.CategoryId}";
}

public sealed record ArchiveProductCommand(ProductId ProductId);

public interface IArchiveProductHandler
{
    Task<Result<bool>> ExecuteAsync(ArchiveProductCommand command, CancellationToken cancellationToken);
}

/// <summary>Takes a product out of use without erasing it. Doing it twice is not an error.</summary>
public sealed class ArchiveProductHandler : IArchiveProductHandler
{
    private readonly IProductRepository _products;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;

    public ArchiveProductHandler(
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

    public async Task<Result<bool>> ExecuteAsync(ArchiveProductCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var product = await _products.GetByIdAsync(command.ProductId, cancellationToken).ConfigureAwait(false);
        if (product is null)
        {
            return Result.Failure<bool>("catalog.product.not-found", "این کالا پیدا نشد؛ ممکن است قبلاً حذف شده باشد.");
        }

        if (product.Status == ProductStatus.Archived)
        {
            return Result.Success(true);
        }

        product.Archive();
        await _products.UpdateAsync(product, cancellationToken).ConfigureAwait(false);
        await _audit.WriteAsync(
            new AuditEntry(
                Guid.NewGuid(),
                _userContext.UserId,
                "catalog.product.archived",
                nameof(Product),
                product.Id.ToString(),
                null,
                product.Name,
                _clock.UtcNow),
            cancellationToken).ConfigureAwait(false);
        await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(true);
    }
}
