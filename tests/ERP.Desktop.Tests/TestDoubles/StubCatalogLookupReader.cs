using ERP.Application.Catalog;

namespace ERP.Desktop.Tests.TestDoubles;

internal sealed class StubCatalogLookupReader : ICatalogLookupReader
{
    private CatalogLookupSnapshot _snapshot;

    public StubCatalogLookupReader(CatalogLookupSnapshot? snapshot = null)
    {
        _snapshot = snapshot ?? new CatalogLookupSnapshot([], [], []);
    }

    public int LoadCount { get; private set; }

    public CatalogLookupSnapshot Snapshot
    {
        get => _snapshot;
        set => _snapshot = value;
    }

    public Task<CatalogLookupSnapshot> LoadAsync(CancellationToken cancellationToken)
    {
        LoadCount++;
        return Task.FromResult(_snapshot);
    }
}
