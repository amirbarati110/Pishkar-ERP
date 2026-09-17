using ERP.Application.Sales;
using ERP.Application.Tests.TestDoubles;
using ERP.Domain.Catalog;
using ERP.Domain.Common;
using ERP.Domain.Customers;
using ERP.Domain.Inventory;
using ERP.Domain.Sales;

namespace ERP.Application.Tests.Sales;

public sealed class SaleReadTests
{
    [Fact]
    public async Task TheDayListAsksStorageForTheTehranDayAndTotalsByPaymentMethod()
    {
        var reader = new FakeSaleReadReader();
        reader.Completed.Add(Item(1258, PaymentMethod.Cash, 100_000));
        reader.Completed.Add(Item(1259, PaymentMethod.Card, 250_000));
        reader.Completed.Add(Item(1260, PaymentMethod.Cash, 50_000));
        reader.Completed.Add(Item(1100, PaymentMethod.Cash, tomans: null)); // فاکتور قدیمی بدون مبلغ ثبت‌شده

        var day = PersianDate.Create(1405, 6, 23);
        var result = await new ListSalesOfDayHandler(reader)
            .ExecuteAsync(new ListSalesOfDayQuery(day), CancellationToken.None);

        Assert.Equal(day.StartUtc, reader.RequestedFrom);
        Assert.Equal(day.EndUtc, reader.RequestedTo);
        Assert.Equal(4, result.Sales.Count);
        Assert.Equal(400_000, result.Total.ToTomansExact());
        Assert.Equal(150_000, result.TotalsByPaymentMethod[PaymentMethod.Cash].ToTomansExact());
        Assert.Equal(250_000, result.TotalsByPaymentMethod[PaymentMethod.Card].ToTomansExact());
        Assert.False(result.TotalsByPaymentMethod.ContainsKey(PaymentMethod.Credit));
        Assert.Equal(1, result.InvoicesWithoutTotal);
    }

    [Fact]
    public async Task DetailsCarryProductNamesUnitsStockAndTheCustomerName()
    {
        var context = new ApplicationTestContext();
        var customer = Customer.QuickCreate("محمد رضایی", "09123456789");
        context.Customers.Items.Add(customer);

        var warehouseId = WarehouseId.New();
        var walnut = ProductId.New();
        var sale = Sale.OpenDraft(warehouseId, customer.Id, context.Clock.UtcNow);
        sale.AddOrIncreaseLine(walnut, Quantity.Create(1), Money.FromTomans(1_250_000));
        context.Sales.Items.Add(sale);

        var reader = new FakeSaleReadReader();
        reader.Products[walnut] = new SaleProductInfo(walnut, "گردو ایرانی ممتاز", "1001", "کیلوگرم", StockBalance.From(12));

        var result = await new GetSaleDetailsHandler(context.Sales, reader, context.Customers)
            .ExecuteAsync(new GetSaleDetailsQuery(sale.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var details = result.Value!;
        Assert.Equal("محمد رضایی", details.CustomerName);
        Assert.Null(details.Number);
        Assert.Null(details.Totals);
        var line = Assert.Single(details.Lines);
        Assert.Equal("گردو ایرانی ممتاز", line.ProductName);
        Assert.Equal("1001", line.Sku);
        Assert.Equal("کیلوگرم", line.UnitSymbol);
        Assert.Equal(12, line.Available.Value);
        Assert.Equal(1_250_000, line.LineTotal.ToTomansExact());
        Assert.Equal(warehouseId, reader.RequestedWarehouse);
    }

    [Fact]
    public async Task ARowWhoseProductIsGoneStillShowsItsAmount()
    {
        var context = new ApplicationTestContext();
        var sale = Sale.OpenDraft(WarehouseId.New(), null, context.Clock.UtcNow);
        sale.AddOrIncreaseLine(ProductId.New(), Quantity.Create(2), Money.FromTomans(10_000));
        context.Sales.Items.Add(sale);

        var result = await new GetSaleDetailsHandler(context.Sales, new FakeSaleReadReader(), context.Customers)
            .ExecuteAsync(new GetSaleDetailsQuery(sale.Id), CancellationToken.None);

        var line = Assert.Single(result.Value!.Lines);
        Assert.Equal("کالای حذف‌شده", line.ProductName);
        Assert.Equal(20_000, line.LineTotal.ToTomansExact());
        Assert.Null(result.Value.CustomerName);
    }

    private static SaleListItem Item(long number, PaymentMethod method, long? tomans)
    {
        var at = new DateTimeOffset(2026, 9, 14, 8, 0, 0, TimeSpan.Zero);
        return new SaleListItem(
            SaleId.New(),
            SaleNumber.From(number),
            at,
            at,
            null,
            null,
            1,
            tomans is { } value ? Money.FromTomans(value) : null,
            method);
    }

    private sealed class FakeSaleReadReader : ISaleReadReader
    {
        public Dictionary<ProductId, SaleProductInfo> Products { get; } = [];

        public List<SaleListItem> Completed { get; } = [];

        public WarehouseId? RequestedWarehouse { get; private set; }

        public DateTimeOffset? RequestedFrom { get; private set; }

        public DateTimeOffset? RequestedTo { get; private set; }

        public Task<IReadOnlyDictionary<ProductId, SaleProductInfo>> ReadProductsAsync(
            WarehouseId warehouseId,
            IReadOnlyCollection<ProductId> productIds,
            CancellationToken cancellationToken)
        {
            RequestedWarehouse = warehouseId;
            return Task.FromResult<IReadOnlyDictionary<ProductId, SaleProductInfo>>(
                Products.Where(pair => productIds.Contains(pair.Key)).ToDictionary());
        }

        public Task<IReadOnlyList<SaleListItem>> ListCompletedAsync(
            DateTimeOffset fromUtc,
            DateTimeOffset toUtc,
            CancellationToken cancellationToken)
        {
            RequestedFrom = fromUtc;
            RequestedTo = toUtc;
            return Task.FromResult<IReadOnlyList<SaleListItem>>(Completed);
        }

        public Task<IReadOnlyList<SaleListItem>> ListCompletedByWarehouseAsync(
            WarehouseId warehouseId,
            DateTimeOffset fromUtc,
            DateTimeOffset toUtc,
            CancellationToken cancellationToken)
        {
            RequestedFrom = fromUtc;
            RequestedTo = toUtc;
            return Task.FromResult<IReadOnlyList<SaleListItem>>(Completed);
        }

        public Task<IReadOnlyList<SaleListItem>> ListDraftsWithItemsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<SaleListItem>>([]);
        }

        public ProductListCriteria? LastCriteria { get; private set; }

        public Task<(IReadOnlyList<SaleProductListItem> Items, int TotalCount)> BrowseProductsAsync(
            ProductListCriteria criteria,
            CancellationToken cancellationToken)
        {
            LastCriteria = criteria;
            return Task.FromResult<(IReadOnlyList<SaleProductListItem>, int)>(([], 31));
        }

        public Task<LineEditInfo> ReadLineEditInfoAsync(
            WarehouseId warehouseId,
            ProductId productId,
            CustomerId? customerId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new LineEditInfo(null, null, null));
        }
    }

    [Fact]
    public async Task BrowsingTurnsPagesIntoOffsetsAndLooksBackThirtyDaysForTopSellers()
    {
        var context = new ApplicationTestContext();
        var reader = new FakeSaleReadReader();

        var page = await new BrowseProductsForSaleHandler(reader, context.Clock).ExecuteAsync(
            new BrowseProductsForSaleQuery(WarehouseId.New(), null, ProductListFilter.TopSelling, Page: 3, PageSize: 15),
            CancellationToken.None);

        Assert.Equal(30, reader.LastCriteria!.Offset);
        Assert.Equal(15, reader.LastCriteria.Limit);
        Assert.Equal(context.Clock.UtcNow.AddDays(-30), reader.LastCriteria.SoldSinceUtc);
        Assert.Equal(3, page.PageCount); // ۳۱ کالا در صفحه‌های ۱۵تایی
    }

    [Fact]
    public async Task CancellingADraftKeepsItAsCancelledAndAuditsWhatWasInTheCart()
    {
        var context = new ApplicationTestContext();
        var sale = Sale.OpenDraft(WarehouseId.New(), null, context.Clock.UtcNow);
        sale.AddOrIncreaseLine(ProductId.New(), Quantity.Create(2), Money.FromTomans(10_000));
        context.Sales.Items.Add(sale);

        var result = await new CancelSaleHandler(context.Sales, context.Audit, context.UnitOfWork, context.User, context.Clock)
            .ExecuteAsync(new CancelSaleCommand(sale.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SaleStatus.Cancelled, sale.Status);
        Assert.Equal("sales.sale.cancelled", Assert.Single(context.Audit.Entries).Action);
    }

    [Fact]
    public async Task ACompletedInvoiceCannotBeCancelled()
    {
        var context = new ApplicationTestContext();
        var sale = Sale.OpenDraft(WarehouseId.New(), null, context.Clock.UtcNow);
        sale.AddOrIncreaseLine(ProductId.New(), Quantity.Create(1), Money.FromTomans(10_000));
        sale.Complete(SaleNumber.From(1), PaymentMethod.Cash, 0, context.Clock.UtcNow);
        context.Sales.Items.Add(sale);

        var result = await new CancelSaleHandler(context.Sales, context.Audit, context.UnitOfWork, context.User, context.Clock)
            .ExecuteAsync(new CancelSaleCommand(sale.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("sales.sale.not-draft", result.Error?.Code);
        Assert.Equal(0, context.UnitOfWork.CommitCount);
    }
}
