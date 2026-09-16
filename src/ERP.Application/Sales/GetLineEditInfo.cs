using ERP.Domain.Catalog;
using ERP.Domain.Customers;
using ERP.Domain.Inventory;

namespace ERP.Application.Sales;

public sealed record GetLineEditInfoQuery(WarehouseId WarehouseId, ProductId ProductId, CustomerId? CustomerId);

public interface IGetLineEditInfoHandler
{
    Task<LineEditInfo> ExecuteAsync(GetLineEditInfoQuery query, CancellationToken cancellationToken);
}

/// <summary>Thin pass-through to <see cref="ISaleReadReader.ReadLineEditInfoAsync"/> — see <see cref="LineEditInfo"/> for what it answers and why.</summary>
public sealed class GetLineEditInfoHandler : IGetLineEditInfoHandler
{
    private readonly ISaleReadReader _reader;

    public GetLineEditInfoHandler(ISaleReadReader reader)
    {
        _reader = reader;
    }

    public Task<LineEditInfo> ExecuteAsync(GetLineEditInfoQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return _reader.ReadLineEditInfoAsync(query.WarehouseId, query.ProductId, query.CustomerId, cancellationToken);
    }
}
