using ERP.Domain.Catalog;

namespace ERP.Application.Catalog;

public sealed record CategoryLookupItem(
    CategoryId Id,
    string Name,
    CategoryId? ParentId,
    int SortOrder);

public sealed record UnitLookupItem(
    UnitId Id,
    string Name,
    string Symbol,
    bool AllowsFractions);

public sealed record ProductLookupItem(
    ProductId Id,
    string Name,
    string? Sku,
    string? PrimaryBarcode);

public sealed record CatalogLookupSnapshot(
    IReadOnlyList<CategoryLookupItem> Categories,
    IReadOnlyList<UnitLookupItem> Units,
    IReadOnlyList<ProductLookupItem> Products);

public interface ICatalogLookupReader
{
    Task<CatalogLookupSnapshot> LoadAsync(CancellationToken cancellationToken);
}
