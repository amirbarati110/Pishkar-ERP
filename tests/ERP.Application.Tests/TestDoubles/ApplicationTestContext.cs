using ERP.Application.Audit;
using ERP.Application.Common;
using ERP.Application.Accounting;
using ERP.Application.Backups;
using ERP.Application.Catalog;
using ERP.Application.Cashiering;
using ERP.Application.Customers;
using ERP.Application.Identity;
using ERP.Application.Inventory;
using ERP.Application.Sales;
using ERP.Domain.Accounting;
using ERP.Domain.Backups;
using ERP.Domain.Cashiering;
using ERP.Domain.Catalog;
using ERP.Domain.Common;
using ERP.Domain.Customers;
using ERP.Domain.Identity;
using ERP.Domain.Inventory;
using ERP.Domain.Sales;

namespace ERP.Application.Tests.TestDoubles;

internal sealed class ApplicationTestContext
{
    public CategoryRepository Categories { get; } = new();

    public ProductRepository Products { get; } = new();

    public StockLedgerRepository StockLedgers { get; } = new();

    public SaleRepository Sales { get; } = new();

    public SequentialSaleNumberGenerator SaleNumbers { get; } = new(firstNumber: 1258);

    public SaleLineCostRepository SaleLineCosts { get; } = new();

    public SaleReturnRepository SaleReturns { get; } = new();

    public SequentialReturnNumberGenerator ReturnNumbers { get; } = new(firstNumber: 1);

    public CustomerRepository Customers { get; } = new();

    public CustomerLedgerReader CustomerLedger { get; } = new();

    public RecordingAuditWriter Audit { get; } = new();

    public UserRepository Users { get; } = new();

    public CashShiftRepository CashShifts { get; } = new();

    public WarehouseRepository Warehouses { get; } = new();

    public JournalEntryRepository JournalEntries { get; } = new();

    public BackupRecordRepository BackupRecords { get; } = new();

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

    public Task AddBarcodeAsync(ProductId productId, string barcode, CancellationToken cancellationToken)
    {
        ExistingBarcodes.Add(barcode.Trim());
        return Task.CompletedTask;
    }

    public int UpdateCount { get; private set; }

    public DataConflictException? ConflictOnUpdate { get; set; }

    public Task UpdateAsync(Product product, CancellationToken cancellationToken)
    {
        if (ConflictOnUpdate is not null)
        {
            throw ConflictOnUpdate;
        }

        UpdateCount++;
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

internal sealed class SaleRepository : ISaleRepository
{
    public List<Sale> Items { get; } = [];

    public int SaveCount { get; private set; }

    public Task<Sale?> GetAsync(SaleId saleId, CancellationToken cancellationToken)
    {
        var sale = Items.SingleOrDefault(item => item.Id == saleId);
        return Task.FromResult(sale);
    }

    public Task SaveAsync(Sale sale, CancellationToken cancellationToken)
    {
        SaveCount++;
        if (!Items.Contains(sale))
        {
            Items.Add(sale);
        }

        return Task.CompletedTask;
    }

    public Task<bool> HasCorrectionAsync(SaleId saleId, CancellationToken cancellationToken) =>
        Task.FromResult(Items.Any(item => item.CorrectsSaleId == saleId));
}

internal sealed class CustomerRepository : ICustomerRepository
{
    public List<Customer> Items { get; } = [];

    public Task<Customer?> GetByIdAsync(CustomerId customerId, CancellationToken cancellationToken)
    {
        return Task.FromResult(Items.SingleOrDefault(item => item.Id == customerId));
    }

    public Task<Customer?> FindByMobileAsync(string normalizedMobile, CancellationToken cancellationToken)
    {
        return Task.FromResult(Items.SingleOrDefault(item => item.Mobile == normalizedMobile));
    }

    public Task<Customer?> FindByNationalIdAsync(string normalizedNationalId, CancellationToken cancellationToken)
    {
        return Task.FromResult(Items.FirstOrDefault(item => item.Profile.NationalId == normalizedNationalId));
    }

    public Task AddAsync(Customer customer, CancellationToken cancellationToken)
    {
        Items.Add(customer);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Customer customer, CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class CustomerLedgerReader : ICustomerLedgerReader
{
    public List<CreditInvoice> CreditInvoices { get; } = [];

    public List<Money> Payments { get; } = [];

    public Task<CustomerLedgerEntries> ReadAsync(CustomerId customerId, CancellationToken cancellationToken)
    {
        return Task.FromResult(new CustomerLedgerEntries(CreditInvoices.ToList(), Payments.ToList()));
    }
}

internal sealed class SequentialSaleNumberGenerator(long firstNumber) : ISaleNumberGenerator
{
    private long _next = firstNumber;

    public int IssuedCount { get; private set; }

    public Task<SaleNumber> NextAsync(CancellationToken cancellationToken)
    {
        IssuedCount++;
        return Task.FromResult(SaleNumber.From(_next++));
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

internal sealed class UserRepository : IUserRepository
{
    public List<User> Items { get; } = [];

    public Task<User?> GetByIdAsync(UserId id, CancellationToken cancellationToken)
    {
        return Task.FromResult(Items.SingleOrDefault(item => item.Id == id));
    }

    public Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken)
    {
        return Task.FromResult(Items.SingleOrDefault(item =>
            string.Equals(item.Username, username, StringComparison.OrdinalIgnoreCase)));
    }

    public Task<bool> AnyExistsAsync(CancellationToken cancellationToken) => Task.FromResult(Items.Count > 0);

    public Task SaveAsync(User user, CancellationToken cancellationToken)
    {
        if (!Items.Contains(user))
        {
            Items.Add(user);
        }

        return Task.CompletedTask;
    }
}

internal sealed class CashShiftRepository : ICashShiftRepository
{
    public List<CashShift> Items { get; } = [];

    public Task<CashShift?> GetOpenAsync(WarehouseId warehouseId, CancellationToken cancellationToken) =>
        Task.FromResult(Items.SingleOrDefault(shift =>
            shift.WarehouseId == warehouseId && shift.Status == CashShiftStatus.Open));

    public Task<CashShift?> GetByIdAsync(CashShiftId id, CancellationToken cancellationToken) =>
        Task.FromResult(Items.SingleOrDefault(shift => shift.Id == id));

    public Task SaveAsync(CashShift shift, CancellationToken cancellationToken)
    {
        if (!Items.Contains(shift))
        {
            Items.Add(shift);
        }

        return Task.CompletedTask;
    }
}

internal sealed class JournalEntryRepository : IJournalEntryRepository
{
    public List<JournalEntry> Items { get; } = [];

    public Task SaveAsync(JournalEntry entry, CancellationToken cancellationToken)
    {
        Items.Add(entry);
        return Task.CompletedTask;
    }

    public Task<JournalEntry?> GetBySourceAsync(JournalSourceType sourceType, string sourceId, CancellationToken cancellationToken) =>
        Task.FromResult(Items.SingleOrDefault(entry => entry.SourceType == sourceType && entry.SourceId == sourceId));
}

internal sealed class BackupRecordRepository : IBackupRecordRepository
{
    public List<BackupRecord> Items { get; } = [];

    public Task SaveAsync(BackupRecord record, CancellationToken cancellationToken)
    {
        if (!Items.Contains(record))
        {
            Items.Add(record);
        }

        return Task.CompletedTask;
    }

    public Task<BackupRecord?> GetByIdAsync(BackupRecordId id, CancellationToken cancellationToken) =>
        Task.FromResult(Items.SingleOrDefault(record => record.Id == id));

    public Task<BackupRecord?> GetLatestAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Items.OrderByDescending(record => record.CreatedAtUtc).FirstOrDefault());

    public Task<IReadOnlyList<BackupRecord>> ListRecentAsync(int count, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<BackupRecord>>(
            Items.OrderByDescending(record => record.CreatedAtUtc).Take(count).ToList());
}

internal sealed record TestUserContext(Guid UserId) : IUserContext;

internal sealed record TestClock(DateTimeOffset UtcNow) : IClock;

internal sealed class SaleLineCostRepository : ISaleLineCostRepository
{
    public Dictionary<SaleId, List<SaleLineCost>> Recorded { get; } = [];

    public Task RecordAsync(SaleId saleId, IReadOnlyCollection<SaleLineCost> costs, CancellationToken cancellationToken)
    {
        Recorded[saleId] = costs.ToList();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<ProductId, Money>> GetUnitCostsAsync(SaleId saleId, CancellationToken cancellationToken)
    {
        var result = new Dictionary<ProductId, Money>();
        if (Recorded.TryGetValue(saleId, out var costs))
        {
            foreach (var cost in costs)
            {
                result[cost.ProductId] = cost.TotalCost.Multiply(1m / cost.CostedQuantity);
            }
        }

        return Task.FromResult<IReadOnlyDictionary<ProductId, Money>>(result);
    }
}

internal sealed class SaleReturnRepository : ISaleReturnRepository
{
    public List<SaleReturn> Items { get; } = [];

    public Task AddAsync(SaleReturn saleReturn, CancellationToken cancellationToken)
    {
        Items.Add(saleReturn);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<ProductId, PreviouslyReturned>> GetReturnedAsync(SaleId saleId, CancellationToken cancellationToken)
    {
        var result = Items
            .Where(item => item.SaleId == saleId)
            .SelectMany(item => item.Lines)
            .GroupBy(line => line.ProductId)
            .ToDictionary(
                group => group.Key,
                group => new PreviouslyReturned(
                    group.Sum(line => line.Quantity.Value),
                    Money.FromRials(group.Sum(line => line.Net.Rials)),
                    Money.FromRials(group.Sum(line => line.Tax.Rials))));
        return Task.FromResult<IReadOnlyDictionary<ProductId, PreviouslyReturned>>(result);
    }

    public Task<bool> AnyForSaleAsync(SaleId saleId, CancellationToken cancellationToken) =>
        Task.FromResult(Items.Any(item => item.SaleId == saleId));
}

internal sealed class SequentialReturnNumberGenerator(long firstNumber) : IReturnNumberGenerator
{
    private long _next = firstNumber;

    public Task<ReturnNumber> NextAsync(CancellationToken cancellationToken) =>
        Task.FromResult(ReturnNumber.From(_next++));
}

internal sealed class WarehouseRepository : IWarehouseRepository
{
    public List<Warehouse> Items { get; } = [];

    public HashSet<WarehouseId> WithStock { get; } = [];

    public HashSet<WarehouseId> WithOpenShift { get; } = [];

    public int UpdateCount { get; private set; }

    public DataConflictException? ConflictOnAdd { get; set; }

    public Task<Warehouse?> GetByIdAsync(WarehouseId warehouseId, CancellationToken cancellationToken) =>
        Task.FromResult(Items.SingleOrDefault(item => item.Id == warehouseId));

    public Task<Warehouse?> FindActiveByNameAsync(string name, CancellationToken cancellationToken) =>
        Task.FromResult(Items.SingleOrDefault(item => item.Status == WarehouseStatus.Active && item.Name == name));

    public Task AddAsync(Warehouse warehouse, CancellationToken cancellationToken)
    {
        if (ConflictOnAdd is not null)
        {
            throw ConflictOnAdd;
        }

        Items.Add(warehouse);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Warehouse warehouse, CancellationToken cancellationToken)
    {
        UpdateCount++;
        return Task.CompletedTask;
    }

    public Task<int> CountActiveAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Items.Count(item => item.Status == WarehouseStatus.Active));

    public Task<bool> HasStockAsync(WarehouseId warehouseId, CancellationToken cancellationToken) =>
        Task.FromResult(WithStock.Contains(warehouseId));

    public Task<bool> HasOpenCashShiftAsync(WarehouseId warehouseId, CancellationToken cancellationToken) =>
        Task.FromResult(WithOpenShift.Contains(warehouseId));
}
