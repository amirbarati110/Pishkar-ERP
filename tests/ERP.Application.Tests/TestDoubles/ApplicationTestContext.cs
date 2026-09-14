using ERP.Application.Audit;
using ERP.Application.Common;
using ERP.Application.Catalog;
using ERP.Application.Inventory;
using ERP.Domain.Catalog;
using ERP.Domain.Inventory;

namespace ERP.Application.Tests.TestDoubles;

internal sealed class ApplicationTestContext
{
    public CategoryRepository Categories { get; } = new();

    public ProductRepository Products { get; } = new();

    public StockLedgerRepository StockLedgers { get; } = new();

    public RecordingAuditWriter Audit { get; } = new();

    public RecordingUnitOfWork UnitOfWork { get; } = new();

    public TestUserContext User { get; } = new(Guid.Parse("6cfba76d-c4cf-4bd9-9dad-952321b87075"));

    public TestClock Clock { get; } = new(new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.Zero));
}

internal sealed class CategoryRepository : ICategoryRepository
{
    public List<Category> Items { get; } = [];

    public Task<Category?> GetByIdAsync(
        CategoryId categoryId,
        CancellationToken cancellationToken)
    {
        var category = Items.SingleOrDefault(item => item.Id == categoryId);
        return Task.FromResult(category);
    }

    public Task<bool> SiblingNameExistsAsync(
        string name,
        CategoryId? parentId,
        CancellationToken cancellationToken)
    {
        var exists = Items.Any(item =>
            item.ParentId == parentId &&
            string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(exists);
    }

    public Task AddAsync(Category category, CancellationToken cancellationToken)
    {
        Items.Add(category);
        return Task.CompletedTask;
    }
}

internal sealed class ProductRepository : IProductRepository
{
    public List<Product> Items { get; } = [];

    public HashSet<string> ExistingBarcodes { get; } = new(StringComparer.OrdinalIgnoreCase);

    public DataConflictException? ConflictOnAdd { get; set; }

    public Task<Product?> GetByIdAsync(
        ProductId productId,
        CancellationToken cancellationToken)
    {
        var product = Items.SingleOrDefault(item => item.Id == productId);
        return Task.FromResult(product);
    }

    public Task<bool> BarcodeExistsAsync(string barcode, CancellationToken cancellationToken)
    {
        return Task.FromResult(ExistingBarcodes.Contains(barcode.Trim()));
    }

    public Task AddAsync(Product product, CancellationToken cancellationToken)
    {
        if (ConflictOnAdd is not null)
        {
            throw ConflictOnAdd;
        }

        Items.Add(product);
        return Task.CompletedTask;
    }
}

internal sealed class StockLedgerRepository : IStockLedgerRepository
{
    public List<StockLedger> Items { get; } = [];

    public int SaveCount { get; private set; }

    public Task<StockLedger?> GetAsync(
        ProductId productId,
        WarehouseId warehouseId,
        CancellationToken cancellationToken)
    {
        var ledger = Items.SingleOrDefault(item =>
            item.ProductId == productId && item.WarehouseId == warehouseId);
        return Task.FromResult(ledger);
    }

    public Task SaveAsync(StockLedger ledger, CancellationToken cancellationToken)
    {
        SaveCount++;
        var existing = Items.SingleOrDefault(item =>
            item.ProductId == ledger.ProductId && item.WarehouseId == ledger.WarehouseId);

        if (existing is null)
        {
            Items.Add(ledger);
        }

        return Task.CompletedTask;
    }
}

internal sealed class RecordingAuditWriter : IAuditWriter
{
    public List<AuditEntry> Entries { get; } = [];

    public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        Entries.Add(entry);
        return Task.CompletedTask;
    }
}

internal sealed class RecordingUnitOfWork : IUnitOfWork
{
    public int CommitCount { get; private set; }

    public Task CommitAsync(CancellationToken cancellationToken)
    {
        CommitCount++;
        return Task.CompletedTask;
    }
}

internal sealed record TestUserContext(Guid UserId) : IUserContext;

internal sealed record TestClock(DateTimeOffset UtcNow) : IClock;
