using ERP.Domain.Catalog;
using ERP.Domain.Inventory;

namespace ERP.Application.Sales;

public sealed record ReadSaleProductsQuery(WarehouseId WarehouseId, IReadOnlyCollection<ProductId> ProductIds);

public interface IReadSaleProductsHandler
{
    Task<IReadOnlyDictionary<ProductId, SaleProductInfo>> ExecuteAsync(
        ReadSaleProductsQuery query,
        CancellationToken cancellationToken);
}

/// <summary>
/// Unit and stock on hand for products the sales screen already has from a
/// search, so a search result row shows «موجودی» like a browsed one.
/// </summary>
public sealed class ReadSaleProductsHandler : IReadSaleProductsHandler
{
    private readonly ISaleReadReader _reader;

    public ReadSaleProductsHandler(ISaleReadReader reader)
    {
        _reader = reader;
    }

    public Task<IReadOnlyDictionary<ProductId, SaleProductInfo>> ExecuteAsync(
        ReadSaleProductsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return _reader.ReadProductsAsync(query.WarehouseId, query.ProductIds, cancellationToken);
    }
}
