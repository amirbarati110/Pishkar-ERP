using ERP.Application.Catalog;
using ERP.Application.Common;
using ERP.Application.Inventory;
using ERP.Domain.Common;
using ERP.Persistence.Catalog;
using ERP.Persistence.Database;
using ERP.Persistence.Services;
using ERP.Persistence.Tests.TestSupport;

namespace ERP.Persistence.Tests.Catalog;

/// <summary>
/// «لیست کالاها» on a real Firebird: the list with half-word search, the category filter
/// that walks the tree down, stock and footer totals, editing, duplicate codes, and «حذف»
/// (archive) that removes a product from the list without erasing it.
/// </summary>
public sealed class ProductListEndToEndTests
{
    [Fact]
    public async Task ListSearchFilterEditAndArchiveWorkTogether()
    {
        await using var database = FirebirdTestDatabase.Create();
        var factory = new FirebirdConnectionFactory(database.Options);
        var defaults = await new FirebirdDatabaseBootstrapper(factory).InitializeAsync(CancellationToken.None);
        var clock = new TestClock { UtcNow = new DateTimeOffset(2026, 9, 20, 8, 0, 0, TimeSpan.Zero) };
        var setup = new FirebirdRetailSetupService(factory, new TestUserContext(Guid.NewGuid()), clock, AllowAllAccess.Instance);
        var list = new ListProductsHandler(new FirebirdProductListReader(factory));

        var food = (await setup.ExecuteAsync(new CreateCategoryCommand("مواد غذایی", null, 1), CancellationToken.None)).Value;
        var dried = (await setup.ExecuteAsync(new CreateCategoryCommand("خشکبار", food, 1), CancellationToken.None)).Value;
        var walnut = (await setup.ExecuteAsync(new CreateCategoryCommand("گردو", dried, 1), CancellationToken.None)).Value;

        var rice = (await setup.ExecuteAsync(
            new CreateProductCommand("برنج هاشمی ۱۰ کیلو", "RICE-1", food, defaults.EachUnitId, Money.FromTomans(1_060_000), ["6260000002001"]),
            CancellationToken.None)).Value;
        var nut = (await setup.ExecuteAsync(
            new CreateProductCommand("گردو ایرانی ۵۰۰ گرم", "NUT-1", walnut, defaults.EachUnitId, Money.FromTomans(420_000), ["6260000002002"]),
            CancellationToken.None)).Value;
        var raisin = (await setup.ExecuteAsync(
            new CreateProductCommand("کشمش پلویی", "RAI-1", dried, defaults.EachUnitId, Money.FromTomans(295_000), []),
            CancellationToken.None)).Value;

        await setup.ExecuteAsync(
            new ReceiveOpeningStockCommand(nut, defaults.MainWarehouseId, 6, 3_000_000, new DateOnly(2026, 9, 1)), CancellationToken.None);
        await setup.ExecuteAsync(
            new ReceiveOpeningStockCommand(rice, defaults.MainWarehouseId, 4, 9_000_000, new DateOnly(2026, 9, 1)), CancellationToken.None);

        // همه‌ی کالاها — به ترتیب نام، با دسته، واحد و موجودی، و جمع پایین
        var all = await list.ExecuteAsync(new ProductListQuery(null, null), CancellationToken.None);
        Assert.Equal(3, all.TotalCount);
        Assert.Equal(10m, all.TotalStock);
        Assert.Equal(3, all.Rows.Count);
        var nutRow = all.Rows.Single(row => row.Id == nut);
        Assert.Equal("گردو", nutRow.CategoryName);
        Assert.Equal(6m, nutRow.Stock);
        Assert.Equal(420_000, nutRow.SalePrice.ToTomansExact());
        Assert.Equal("6260000002002", nutRow.PrimaryBarcode);
        Assert.Equal(0m, all.Rows.Single(row => row.Id == raisin).Stock);

        // جست‌وجوی نیم‌کلمه‌ی فارسی، کد، و بارکد
        var half = await list.ExecuteAsync(new ProductListQuery("ردو", null), CancellationToken.None);
        Assert.Equal(nut, Assert.Single(half.Rows).Id);
        var byCode = await list.ExecuteAsync(new ProductListQuery("rai-", null), CancellationToken.None);
        Assert.Equal(raisin, Assert.Single(byCode.Rows).Id);
        var byBarcode = await list.ExecuteAsync(new ProductListQuery("2001", null), CancellationToken.None);
        Assert.Equal(rice, Assert.Single(byBarcode.Rows).Id);
        var none = await list.ExecuteAsync(new ProductListQuery("چیزی-که-نیست", null), CancellationToken.None);
        Assert.Empty(none.Rows);
        Assert.Equal(0, none.TotalCount);

        // فیلتر دسته‌بندی، زیردسته‌ها هم می‌آیند: «خشکبار» = کشمش + گردو، ولی برنج نه
        var driedList = await list.ExecuteAsync(new ProductListQuery(null, dried), CancellationToken.None);
        Assert.Equal(2, driedList.Rows.Count);
        Assert.Contains(driedList.Rows, row => row.Id == nut);
        Assert.Contains(driedList.Rows, row => row.Id == raisin);
        Assert.Equal(6m, driedList.TotalStock);
        var walnutOnly = await list.ExecuteAsync(new ProductListQuery(null, walnut), CancellationToken.None);
        Assert.Equal(nut, Assert.Single(walnutOnly.Rows).Id);
        var searchInsideCategory = await list.ExecuteAsync(new ProductListQuery("کشمش", dried), CancellationToken.None);
        Assert.Equal(raisin, Assert.Single(searchInsideCategory.Rows).Id);

        // سقف تعداد ردیف: جمع پایین همچنان همه را می‌شمارد
        var capped = await list.ExecuteAsync(new ProductListQuery(null, null, Take: 1), CancellationToken.None);
        Assert.Single(capped.Rows);
        Assert.Equal(3, capped.TotalCount);

        // ویرایش: نام، کد، دسته، قیمت
        var edited = await setup.ExecuteAsync(
            new UpdateProductCommand(nut, "گردو ایرانی ممتاز ۵۰۰ گرم", "NUT-PLUS", dried, Money.FromTomans(450_000)),
            CancellationToken.None);
        Assert.True(edited.IsSuccess, edited.Error?.Message);
        var afterEdit = (await list.ExecuteAsync(new ProductListQuery("ممتاز", null), CancellationToken.None)).Rows.Single();
        Assert.Equal("NUT-PLUS", afterEdit.Sku);
        Assert.Equal("خشکبار", afterEdit.CategoryName);
        Assert.Equal(450_000, afterEdit.SalePrice.ToTomansExact());
        Assert.Equal(6m, afterEdit.Stock);
        Assert.Equal("6260000002002", afterEdit.PrimaryBarcode); // بارکد دست نخورد

        // کد تکراری کالای دیگر رد می‌شود و چیزی عوض نمی‌شود
        var duplicate = await setup.ExecuteAsync(
            new UpdateProductCommand(raisin, "کشمش پلویی", "RICE-1", dried, Money.FromTomans(295_000)),
            CancellationToken.None);
        Assert.False(duplicate.IsSuccess);
        Assert.Equal("catalog.product.duplicate-sku", duplicate.Error?.Code);
        Assert.Equal("RAI-1", (await list.ExecuteAsync(new ProductListQuery("کشمش", null), CancellationToken.None)).Rows.Single().Sku);

        // نام خالی رد می‌شود
        var blank = await setup.ExecuteAsync(
            new UpdateProductCommand(raisin, "  ", "RAI-1", dried, Money.FromTomans(295_000)), CancellationToken.None);
        Assert.False(blank.IsSuccess);

        // «حذف» = بایگانی: از لیست می‌رود، دوباره‌اش خطا نیست، ویرایشش رد می‌شود
        var archived = await setup.ExecuteAsync(new ArchiveProductCommand(raisin), CancellationToken.None);
        Assert.True(archived.IsSuccess);
        var afterArchive = await list.ExecuteAsync(new ProductListQuery(null, null), CancellationToken.None);
        Assert.Equal(2, afterArchive.TotalCount);
        Assert.DoesNotContain(afterArchive.Rows, row => row.Id == raisin);
        Assert.True((await setup.ExecuteAsync(new ArchiveProductCommand(raisin), CancellationToken.None)).IsSuccess);
        Assert.Equal(
            "catalog.product.archived",
            (await setup.ExecuteAsync(
                new UpdateProductCommand(raisin, "کشمش", null, dried, Money.FromTomans(1)), CancellationToken.None)).Error?.Code);
        Assert.Equal(
            "catalog.product.not-found",
            (await setup.ExecuteAsync(new ArchiveProductCommand(ProductIdOf(Guid.NewGuid())), CancellationToken.None)).Error?.Code);
    }

    private static ERP.Domain.Catalog.ProductId ProductIdOf(Guid id) => ERP.Domain.Catalog.ProductId.From(id);

    private sealed record TestUserContext(Guid UserId) : IUserContext;

    private sealed class TestClock : IClock
    {
        public required DateTimeOffset UtcNow { get; set; }
    }
}
