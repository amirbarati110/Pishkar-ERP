using ERP.Application.Accounting;
using ERP.Application.Audit;
using ERP.Application.Cashiering;
using ERP.Application.Catalog;
using ERP.Application.Common;
using ERP.Application.Customers;
using ERP.Application.Identity;
using ERP.Application.Inventory;
using ERP.Application.Sales;
using ERP.Domain.Accounting;
using ERP.Domain.Cashiering;
using ERP.Domain.Catalog;
using ERP.Domain.Common;
using ERP.Domain.Customers;
using ERP.Domain.Identity;
using ERP.Domain.Inventory;
using ERP.Domain.Sales;
using ERP.Presentation.Features.Sales;

namespace ERP.Desktop.Tests.Features.Sales;

/// <summary>
/// The real Application handlers over in-memory storage, so the sales screen's
/// view model is tested against the actual business rules (credit limit,
/// stock, numbering, totals) — not against canned answers.
/// </summary>
internal sealed class InMemorySalesBackend :
    ISaleRepository, IProductRepository, IStockLedgerRepository, ISaleNumberGenerator,
    ICustomerRepository, ICustomerLedgerReader, ICustomerSearchReader, IAuditWriter, IUnitOfWork,
    ISaleReadReader, IProductSearchReader, ICatalogLookupReader, ICustomerPaymentRepository, ICustomerCashReceiptReader, IUserRepository,
    ICashShiftRepository, IJournalEntryRepository, ISaleLineCostRepository, ISaleReturnRepository,
    IReturnNumberGenerator, IUserContext, IClock
{
    private long _nextNumber = 1258;

    public InMemorySalesBackend()
    {
        Warehouse = WarehouseId.New();
        Category = Domain.Catalog.Category.Create("مواد غذایی", null, 1);
        Unit = Domain.Catalog.Unit.Create("عدد", "عدد", false);
    }

    public WarehouseId Warehouse { get; }

    public Category Category { get; }

    public Unit Unit { get; }

    public List<Sale> Sales { get; } = [];

    public List<Product> Products { get; } = [];

    public List<StockLedger> Ledgers { get; } = [];

    public List<Customer> Customers { get; } = [];

    public List<AuditEntry> Audit { get; } = [];

    public List<CustomerPayment> Payments { get; } = [];

    public List<User> Users { get; } = [];

    public List<CashShift> CashShifts { get; } = [];

    public List<JournalEntry> JournalEntries { get; } = [];

    public List<SaleReturn> SaleReturns { get; } = [];

    public Dictionary<SaleId, List<SaleLineCost>> LineCosts { get; } = [];

    private long _nextReturnNumber = 1;

    public int CompleteCalls { get; set; }

    public Guid UserId { get; } = Guid.NewGuid();

    public DateTimeOffset UtcNow { get; set; } = new(2026, 9, 14, 7, 0, 0, TimeSpan.Zero);

    public Product AddProduct(string name, string sku, string barcode, long tomans, decimal stock)
    {
        var product = Product.Create(name, sku, Category.Id, Unit.Id, Money.FromTomans(tomans));
        product.AddBarcode(barcode);
        Products.Add(product);

        var ledger = StockLedger.Empty(product.Id, Warehouse);
        if (stock > 0)
        {
            ledger.ReceiveOpeningStock(Quantity.Create(stock), Money.FromTomans(tomans / 2), new DateOnly(2026, 9, 1));
        }

        Ledgers.Add(ledger);
        return product;
    }

    public SalesBackend Build()
    {
        return new SalesBackend(
            new StartSaleHandler(this, this, this),
            new AddSaleLineHandler(this, this, this),
            new ChangeSaleLineHandler(this, this, this, this, this, this),
            new RemoveSaleLineHandler(this, this),
            new SetSaleChargesHandler(this, this),
            new SetSaleCustomerHandler(this, this, this),
            new SetSaleNoteHandler(this, this),
            new CountingComplete(this, new CompleteSaleHandler(this, this, this, this, this, this, this, this, this, this, this)),
            new StartSaleCorrectionHandler(this, this, this, this),
            new CompleteSaleCorrectionHandler(this, this, this, this, this, this, this, this, this),
            new CancelSaleHandler(this, this, this, this, this),
            new GetSaleDetailsHandler(this, this, this),
            new ListHeldSalesHandler(this),
            new ListSalesOfDayHandler(this),
            new BrowseProductsForSaleHandler(this, this),
            new ReadSaleProductsHandler(this),
            new GetLineEditInfoHandler(this),
            new SearchProductsHandler(this),
            this,
            new SearchCustomersHandler(this),
            new QuickCreateCustomerHandler(this, this, this, this, this),
            new GetCustomerAccountHandler(this, this),
            new RecordCustomerPaymentHandler(this, this, this, this, this, this, this),
            new VerifyAdminCredentialHandler(this),
            new GetSaleJournalEntryHandler(this),
            new FindSaleForReturnHandler(this),
            new GetReturnableSaleHandler(this, this, this),
            new PreviewSaleReturnHandler(this, this, this),
            new CompleteSaleReturnHandler(this, this, this, this, this, this, this, this, this, this),
            this);
    }

    /// <summary>For §6.7 approval tests: an admin who can pass <see cref="VerifyAdminCredentialHandler"/>.</summary>
    public User AddAdmin(string username, string password)
    {
        var admin = User.Create(username, username, password, UserRole.Admin);
        Users.Add(admin);
        return admin;
    }

    // ── IUserRepository ──

    public Task<User?> GetByIdAsync(UserId id, CancellationToken cancellationToken) =>
        Task.FromResult(Users.SingleOrDefault(user => user.Id == id));

    public Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken) =>
        Task.FromResult(Users.SingleOrDefault(user =>
            string.Equals(user.Username, username?.Trim(), StringComparison.OrdinalIgnoreCase)));

    public Task<bool> AnyExistsAsync(CancellationToken cancellationToken) => Task.FromResult(Users.Count > 0);

    public Task SaveAsync(User user, CancellationToken cancellationToken)
    {
        if (!Users.Contains(user))
        {
            Users.Add(user);
        }

        return Task.CompletedTask;
    }

    // ── ICashShiftRepository ──

    public Task<CashShift?> GetOpenAsync(WarehouseId warehouseId, CancellationToken cancellationToken) =>
        Task.FromResult(CashShifts.SingleOrDefault(shift =>
            shift.WarehouseId == warehouseId && shift.Status == CashShiftStatus.Open));

    public Task<CashShift?> GetByIdAsync(CashShiftId id, CancellationToken cancellationToken) =>
        Task.FromResult(CashShifts.SingleOrDefault(shift => shift.Id == id));

    public Task SaveAsync(CashShift shift, CancellationToken cancellationToken)
    {
        if (!CashShifts.Contains(shift))
        {
            CashShifts.Add(shift);
        }

        return Task.CompletedTask;
    }

    // ── IJournalEntryRepository ──

    public Task SaveAsync(JournalEntry entry, CancellationToken cancellationToken)
    {
        JournalEntries.Add(entry);
        return Task.CompletedTask;
    }

    public Task<JournalEntry?> GetBySourceAsync(JournalSourceType sourceType, string sourceId, CancellationToken cancellationToken) =>
        Task.FromResult(JournalEntries.SingleOrDefault(entry => entry.SourceType == sourceType && entry.SourceId == sourceId));

    // ── ISaleRepository / ISaleNumberGenerator ──
    Task<Sale?> ISaleRepository.GetAsync(SaleId saleId, CancellationToken cancellationToken) =>
        Task.FromResult(Sales.SingleOrDefault(sale => sale.Id == saleId));

    Task ISaleRepository.SaveAsync(Sale sale, CancellationToken cancellationToken)
    {
        if (!Sales.Contains(sale))
        {
            Sales.Add(sale);
        }

        return Task.CompletedTask;
    }

    Task<bool> ISaleRepository.HasCorrectionAsync(SaleId saleId, CancellationToken cancellationToken) =>
        Task.FromResult(Sales.Any(sale => sale.CorrectsSaleId == saleId));

    Task<SaleNumber> ISaleNumberGenerator.NextAsync(CancellationToken cancellationToken) =>
        Task.FromResult(SaleNumber.From(_nextNumber++));

    // ── IProductRepository ──
    Task<Product?> IProductRepository.GetByIdAsync(ProductId productId, CancellationToken cancellationToken) =>
        Task.FromResult(Products.SingleOrDefault(product => product.Id == productId));

    Task<bool> IProductRepository.BarcodeExistsAsync(string barcode, CancellationToken cancellationToken) =>
        Task.FromResult(Products.Any(product => product.Barcodes.Any(item => item.Value == barcode)));

    Task IProductRepository.AddAsync(Product product, CancellationToken cancellationToken)
    {
        Products.Add(product);
        return Task.CompletedTask;
    }

    Task IProductRepository.AddBarcodeAsync(ProductId productId, string barcode, CancellationToken cancellationToken) => Task.CompletedTask;

    Task IProductRepository.UpdateAsync(Product product, CancellationToken cancellationToken) => Task.CompletedTask;

    // ── IStockLedgerRepository ──
    Task<StockLedger?> IStockLedgerRepository.GetAsync(ProductId productId, WarehouseId warehouseId, CancellationToken cancellationToken) =>
        Task.FromResult(Ledgers.SingleOrDefault(ledger => ledger.ProductId == productId && ledger.WarehouseId == warehouseId));

    Task IStockLedgerRepository.SaveAsync(StockLedger ledger, CancellationToken cancellationToken)
    {
        if (!Ledgers.Contains(ledger))
        {
            Ledgers.Add(ledger);
        }

        return Task.CompletedTask;
    }

    // ── customers ──
    Task<Customer?> ICustomerRepository.GetByIdAsync(CustomerId customerId, CancellationToken cancellationToken) =>
        Task.FromResult(Customers.SingleOrDefault(customer => customer.Id == customerId));

    Task<Customer?> ICustomerRepository.FindByMobileAsync(string normalizedMobile, CancellationToken cancellationToken) =>
        Task.FromResult(Customers.SingleOrDefault(customer => customer.Mobile == normalizedMobile));

    Task<Customer?> ICustomerRepository.FindByNationalIdAsync(string normalizedNationalId, CancellationToken cancellationToken) =>
        Task.FromResult(Customers.FirstOrDefault(customer => customer.Profile.NationalId == normalizedNationalId));

    Task ICustomerRepository.AddAsync(Customer customer, CancellationToken cancellationToken)
    {
        Customers.Add(customer);
        return Task.CompletedTask;
    }

    Task ICustomerRepository.UpdateAsync(Customer customer, CancellationToken cancellationToken) => Task.CompletedTask;

    Task<CustomerLedgerEntries> ICustomerLedgerReader.ReadAsync(CustomerId customerId, CancellationToken cancellationToken)
    {
        var invoices = Sales
            .Where(sale => sale.CustomerId == customerId && sale.Status == SaleStatus.Completed && sale.PaymentMethod == PaymentMethod.Credit)
            .Select(sale => new CreditInvoice(sale.Number!.Value.Value, sale.CompletedAtUtc!.Value, sale.Totals!.Total))
            .ToList();
        var payments = Payments
            .Where(payment => payment.CustomerId == customerId)
            .Select(payment => payment.Amount)
            .ToList();
        return Task.FromResult(new CustomerLedgerEntries(invoices, payments));
    }

    Task ICustomerPaymentRepository.AddAsync(CustomerPayment payment, CancellationToken cancellationToken)
    {
        Payments.Add(payment);
        return Task.CompletedTask;
    }

    Task<IReadOnlyList<CustomerSearchResult>> ICustomerSearchReader.SearchAsync(
        string nameTerm, string? mobileDigits, int maxResults, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CustomerSearchResult>>(Customers
            .Where(customer => customer.Name.Contains(nameTerm, StringComparison.Ordinal)
                || (mobileDigits is not null && customer.Mobile.Contains(mobileDigits, StringComparison.Ordinal)))
            .Take(maxResults)
            .Select(customer => new CustomerSearchResult(customer.Id, customer.Name, customer.Mobile))
            .ToList());

    // ── audit / unit of work ──
    Task IAuditWriter.WriteAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        Audit.Add(entry);
        return Task.CompletedTask;
    }

    Task IUnitOfWork.CommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // ── read side ──
    Task<IReadOnlyDictionary<ProductId, SaleProductInfo>> ISaleReadReader.ReadProductsAsync(
        WarehouseId warehouseId, IReadOnlyCollection<ProductId> productIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<ProductId, SaleProductInfo>>(Products
            .Where(product => productIds.Contains(product.Id))
            .ToDictionary(product => product.Id, product => new SaleProductInfo(
                product.Id, product.Name, product.Sku, Unit.Symbol, Available(product.Id))));

    Task<IReadOnlyList<SaleListItem>> ISaleReadReader.ListCompletedAsync(
        DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SaleListItem>>(Sales
            .Where(sale => sale.Status == SaleStatus.Completed && sale.CompletedAtUtc >= fromUtc && sale.CompletedAtUtc < toUtc)
            .OrderByDescending(sale => sale.Number!.Value.Value)
            .Select(sale => ToListItem(sale, sale.Totals?.Total))
            .ToList());

    Task<IReadOnlyList<CompletedSaleCash>> ISaleReadReader.ListTillSalesByWarehouseAsync(
        WarehouseId warehouseId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CompletedSaleCash>>(Sales
            .Where(sale => sale.Status == SaleStatus.Completed && sale.WarehouseId == warehouseId
                && sale.CompletedAtUtc >= fromUtc && sale.CompletedAtUtc < toUtc)
            .Select(sale =>
            {
                var replaced = Sales.FirstOrDefault(other => other.Id == sale.CorrectsSaleId);
                return new CompletedSaleCash(
                    sale.PaymentMethod!.Value, sale.Totals?.Total ?? Money.Zero, replaced?.PaymentMethod, replaced?.Totals?.Total);
            })
            .ToList());

    Task<Money> ICustomerCashReceiptReader.SumCashReceivedAsync(
        WarehouseId warehouseId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken) =>
        Task.FromResult(Money.FromRials(Payments
            .Where(payment => payment.WarehouseId == warehouseId && payment.Method == CustomerPaymentMethod.Cash
                && payment.ReceivedAtUtc >= fromUtc && payment.ReceivedAtUtc < toUtc)
            .Sum(payment => payment.Amount.Rials)));

    Task<IReadOnlyList<SaleListItem>> ISaleReadReader.ListDraftsWithItemsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SaleListItem>>(Sales
            .Where(sale => sale.Status == SaleStatus.Draft && sale.Lines.Count > 0)
            .OrderBy(sale => sale.OpenedAtUtc)
            .Select(sale => ToListItem(sale, sale.Subtotal))
            .ToList());

    Task<(IReadOnlyList<SaleProductListItem> Items, int TotalCount)> ISaleReadReader.BrowseProductsAsync(
        ProductListCriteria criteria, CancellationToken cancellationToken)
    {
        var all = Products
            .Select(product => new SaleProductListItem(product.Id, product.Name, product.Sku, Unit.Symbol, product.SalePrice, Available(product.Id)))
            .Where(item => criteria.Filter != ProductListFilter.LowStock || item.Available.Value <= criteria.LowStockAtOrBelow)
            .Where(item => criteria.Filter != ProductListFilter.TopSelling || Sales.Any(sale =>
                sale.Status == SaleStatus.Completed && sale.Lines.Any(line => line.ProductId == item.Id)))
            .OrderBy(item => item.Name, StringComparer.Ordinal)
            .ToList();
        return Task.FromResult<(IReadOnlyList<SaleProductListItem>, int)>(
            (all.Skip(criteria.Offset).Take(criteria.Limit).ToList(), all.Count));
    }

    Task<SaleListItem?> ISaleReadReader.FindCompletedByNumberAsync(long number, CancellationToken cancellationToken)
    {
        var sale = Sales.FirstOrDefault(item =>
            item.Status == SaleStatus.Completed
            && item.Number?.Value == number
            && !Sales.Any(other => other.CorrectsSaleId == item.Id));
        var effective = sale ?? Sales.FirstOrDefault(item =>
            item.Status == SaleStatus.Completed
            && Sales.Any(original => original.Number?.Value == number && item.CorrectsSaleId == original.Id));
        return Task.FromResult(effective is null
            ? null
            : new SaleListItem(
                effective.Id, effective.Number, effective.OpenedAtUtc, effective.CompletedAtUtc,
                effective.CustomerId, null, effective.Lines.Count, effective.Totals?.Total, effective.PaymentMethod));
    }

    Task<IReadOnlyList<ReturnListItem>> ISaleReadReader.ListReturnsByWarehouseAsync(
        WarehouseId warehouseId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken)
    {
        IReadOnlyList<ReturnListItem> items = SaleReturns
            .Where(item => item.WarehouseId == warehouseId && item.CompletedAtUtc >= fromUtc && item.CompletedAtUtc < toUtc)
            .Select(item => new ReturnListItem(
                item.Id, item.Number, Sales.FirstOrDefault(sale => sale.Id == item.SaleId)?.Number,
                item.CompletedAtUtc, item.RefundMethod, item.RefundTotal))
            .ToList();
        return Task.FromResult(items);
    }

    // ── ISaleLineCostRepository / ISaleReturnRepository / IReturnNumberGenerator ──

    Task ISaleLineCostRepository.RecordAsync(SaleId saleId, IReadOnlyCollection<SaleLineCost> costs, CancellationToken cancellationToken)
    {
        LineCosts[saleId] = costs.ToList();
        return Task.CompletedTask;
    }

    Task<IReadOnlyDictionary<ProductId, Money>> ISaleLineCostRepository.GetUnitCostsAsync(SaleId saleId, CancellationToken cancellationToken)
    {
        var result = new Dictionary<ProductId, Money>();
        if (LineCosts.TryGetValue(saleId, out var costs))
        {
            foreach (var cost in costs)
            {
                result[cost.ProductId] = cost.TotalCost.Multiply(1m / cost.CostedQuantity);
            }
        }

        return Task.FromResult<IReadOnlyDictionary<ProductId, Money>>(result);
    }

    Task ISaleReturnRepository.AddAsync(SaleReturn saleReturn, CancellationToken cancellationToken)
    {
        SaleReturns.Add(saleReturn);
        return Task.CompletedTask;
    }

    Task<IReadOnlyDictionary<ProductId, PreviouslyReturned>> ISaleReturnRepository.GetReturnedAsync(SaleId saleId, CancellationToken cancellationToken)
    {
        var result = SaleReturns
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

    Task<bool> ISaleReturnRepository.AnyForSaleAsync(SaleId saleId, CancellationToken cancellationToken) =>
        Task.FromResult(SaleReturns.Any(item => item.SaleId == saleId));

    Task<bool> ISaleReturnRepository.HasRefundedServiceChargeAsync(SaleId saleId, CancellationToken cancellationToken) =>
        Task.FromResult(SaleReturns.Any(item => item.SaleId == saleId && !item.ServiceCharge.IsEmpty));

    Task<ReturnNumber> IReturnNumberGenerator.NextAsync(CancellationToken cancellationToken) =>
        Task.FromResult(ReturnNumber.From(_nextReturnNumber++));

    Task<LineEditInfo> ISaleReadReader.ReadLineEditInfoAsync(
        WarehouseId warehouseId, ProductId productId, CustomerId? customerId, CancellationToken cancellationToken)
    {
        var layers = Ledgers.SingleOrDefault(ledger => ledger.ProductId == productId)?.Layers ?? [];
        var lastPurchase = layers.OrderByDescending(layer => layer.ReceivedOn).FirstOrDefault();
        var currentCost = layers
            .Where(layer => layer.RemainingQuantity.Value > 0)
            .OrderBy(layer => layer.ReceivedOn)
            .FirstOrDefault();

        Money? lastSalePrice = customerId is null
            ? null
            : Sales
                .Where(sale => sale.Status == SaleStatus.Completed && sale.CustomerId == customerId)
                .OrderByDescending(sale => sale.CompletedAtUtc)
                .SelectMany(sale => sale.Lines.Where(line => line.ProductId == productId).Select(line => line.UnitPrice))
                .Cast<Money?>()
                .FirstOrDefault();

        return Task.FromResult(new LineEditInfo(
            lastPurchase?.UnitCost,
            currentCost?.UnitCost,
            lastSalePrice));
    }

    Task<IReadOnlyList<ProductSearchResult>> IProductSearchReader.SearchAsync(SearchProductsQuery query, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ProductSearchResult>>(Products
            .Where(product => product.Name.Contains(query.Term, StringComparison.Ordinal))
            .Take(query.MaxResults)
            .Select(ToSearchResult)
            .ToList());

    Task<ProductSearchResult?> IProductSearchReader.FindByExactCodeAsync(string code, CancellationToken cancellationToken) =>
        Task.FromResult(Products
            .Where(product => string.Equals(product.Sku, code, StringComparison.OrdinalIgnoreCase)
                || product.Barcodes.Any(barcode => barcode.Value == code))
            .Select(ToSearchResult)
            .FirstOrDefault());

    Task<CatalogLookupSnapshot> ICatalogLookupReader.LoadAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new CatalogLookupSnapshot(
            [new CategoryLookupItem(Category.Id, Category.Name, null, 1)],
            [new UnitLookupItem(Unit.Id, Unit.Name, Unit.Symbol, Unit.AllowsFractions)],
            []));

    private StockBalance Available(ProductId productId) =>
        Ledgers.SingleOrDefault(ledger => ledger.ProductId == productId)?.AvailableQuantity ?? StockBalance.From(0);

    private SaleListItem ToListItem(Sale sale, Money? amount) => new(
        sale.Id, sale.Number, sale.OpenedAtUtc, sale.CompletedAtUtc, sale.CustomerId,
        sale.CustomerId is { } id ? Customers.Single(customer => customer.Id == id).Name : null,
        sale.Lines.Count, amount, sale.PaymentMethod);

    private static ProductSearchResult ToSearchResult(Product product) =>
        new(product.Id, product.Name, product.Sku, product.Barcodes.Count > 0 ? product.Barcodes[0].Value : null, product.SalePrice);

    private sealed class CountingComplete(InMemorySalesBackend owner, ICompleteSaleHandler inner) : ICompleteSaleHandler
    {
        public Task<Result<CompletedSale>> ExecuteAsync(CompleteSaleCommand command, CancellationToken cancellationToken)
        {
            owner.CompleteCalls++;
            return inner.ExecuteAsync(command, cancellationToken);
        }
    }
}
