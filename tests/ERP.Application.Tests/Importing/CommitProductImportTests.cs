using ERP.Application.Importing;
using ERP.Application.Tests.TestDoubles;
using ERP.Domain.Catalog;
using ERP.Domain.Common;
using ERP.Domain.Inventory;

namespace ERP.Application.Tests.Importing;

public sealed class CommitProductImportTests
{
    [Fact]
    public async Task CommitsEveryRowAndTheirOpeningStockInOneGo()
    {
        var context = new ApplicationTestContext();
        var warehouseId = WarehouseId.New();
        var rows = new[]
        {
            Row(1, "برنج ایرانی", quantity: 10, unitCostTomans: 100_000),
            Row(2, "روغن", quantity: null, unitCostTomans: null),
        };

        var result = await Handler(context).ExecuteAsync(
            new CommitProductImportCommand(rows, warehouseId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value);
        Assert.Equal(2, context.Products.Items.Count);
        var stocked = context.Products.Items.Single(p => p.Name == "برنج ایرانی");
        var ledger = context.StockLedgers.Items.Single(l => l.ProductId == stocked.Id);
        Assert.Equal(10, ledger.AvailableQuantity.Value);
        var unstocked = context.Products.Items.Single(p => p.Name == "روغن");
        Assert.DoesNotContain(context.StockLedgers.Items, l => l.ProductId == unstocked.Id);
        Assert.Equal(2, context.Audit.Entries.Count(e => e.Action == "catalog.product.imported"));
        Assert.Equal(1, context.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task ADuplicateBarcodeAgainstTheRealDatabaseRefusesTheWholeBatch()
    {
        var context = new ApplicationTestContext();
        context.Products.ExistingBarcodes.Add("6260001000011");
        var rows = new[]
        {
            Row(1, "برنج ایرانی", quantity: null, unitCostTomans: null),
            Row(2, "روغن", quantity: null, unitCostTomans: null, barcode: "6260001000011"),
        };

        var result = await Handler(context).ExecuteAsync(
            new CommitProductImportCommand(rows, WarehouseId.New()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("import.product.duplicate-barcode", result.Error?.Code);
        // این تست حافظه‌ای CommitAsync را فراخوانی نمی‌کند تأیید می‌کند — یعنی
        // چیزی روی تراکنش «ثبت نهایی» نشده. رول‌بک واقعی (این‌که ردیف اول هم
        // در دیتابیس واقعی باقی نمی‌ماند) روی Firebird واقعی تست می‌شود، چون
        // repository حافظه‌ای اصلاً تراکنش ندارد که برگردد.
        Assert.Equal(0, context.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task AnEmptyBatchIsRefusedRatherThanSilentlyDoingNothing()
    {
        var context = new ApplicationTestContext();

        var result = await Handler(context).ExecuteAsync(
            new CommitProductImportCommand([], WarehouseId.New()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("import.product.nothing-to-import", result.Error?.Code);
    }

    private static PreparedProductImportRow Row(
        int rowNumber, string name, decimal? quantity, long? unitCostTomans, string? barcode = null) =>
        new(
            rowNumber,
            name,
            Sku: null,
            CategoryId.New(),
            UnitId.New(),
            Money.FromTomans(50_000),
            barcode is null ? [] : [barcode],
            quantity,
            unitCostTomans is { } cost ? Money.FromTomans(cost) : null);

    private static CommitProductImportHandler Handler(ApplicationTestContext context) => new(
        context.Products,
        context.StockLedgers,
        context.Audit,
        context.JournalEntries,
        context.UnitOfWork,
        context.User,
        context.Clock);
}
