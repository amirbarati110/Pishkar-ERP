using ERP.Application.Catalog;
using ERP.Application.Common;
using ERP.Application.Tests.TestDoubles;
using ERP.Domain.Catalog;
using ERP.Domain.Common;

namespace ERP.Application.Tests.Catalog;

public sealed class ManageProductsTests
{
    private static Product Existing(ApplicationTestContext context)
    {
        var product = Product.Create("برنج", "RICE", CategoryId.New(), UnitId.New(), Money.FromTomans(100_000));
        context.Products.Items.Add(product);
        return product;
    }

    [Fact]
    public async Task UpdateChangesNameCodeCategoryAndPriceAndWritesAudit()
    {
        var context = new ApplicationTestContext();
        var product = Existing(context);
        var newCategory = CategoryId.New();

        var result = await new UpdateProductHandler(context.Products, context.Audit, context.UnitOfWork, context.User, context.Clock)
            .ExecuteAsync(new UpdateProductCommand(product.Id, "  برنج هاشمی ", "rice-2", newCategory, Money.FromTomans(120_000)), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("برنج هاشمی", product.Name);
        Assert.Equal("RICE-2", product.Sku);
        Assert.Equal(newCategory, product.CategoryId);
        Assert.Equal(120_000, product.SalePrice.ToTomansExact());
        Assert.Equal(1, context.Products.UpdateCount);
        Assert.Equal(1, context.UnitOfWork.CommitCount);
        Assert.Equal("catalog.product.updated", Assert.Single(context.Audit.Entries).Action);
    }

    [Fact]
    public async Task UpdateRejectsBlankNameAndSavesNothing()
    {
        var context = new ApplicationTestContext();
        var product = Existing(context);

        var result = await new UpdateProductHandler(context.Products, context.Audit, context.UnitOfWork, context.User, context.Clock)
            .ExecuteAsync(new UpdateProductCommand(product.Id, " ", null, product.CategoryId, product.SalePrice), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("catalog.product.invalid", result.Error?.Code);
        Assert.Equal(0, context.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task UpdateReportsADuplicateCodeFromTheDatabaseWithoutCommitting()
    {
        var context = new ApplicationTestContext();
        var product = Existing(context);
        context.Products.ConflictOnUpdate = new DataConflictException(
            "catalog.product.duplicate-sku", "این کد کالا قبلاً برای کالای دیگری استفاده شده است.", new InvalidOperationException("uq"));

        var result = await new UpdateProductHandler(context.Products, context.Audit, context.UnitOfWork, context.User, context.Clock)
            .ExecuteAsync(new UpdateProductCommand(product.Id, "برنج", "TAKEN", product.CategoryId, product.SalePrice), CancellationToken.None);

        Assert.Equal("catalog.product.duplicate-sku", result.Error?.Code);
        Assert.Equal(0, context.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task UpdateOfMissingOrArchivedProductFailsClearly()
    {
        var context = new ApplicationTestContext();
        var archived = Existing(context);
        archived.Archive();
        var handler = new UpdateProductHandler(context.Products, context.Audit, context.UnitOfWork, context.User, context.Clock);

        var missing = await handler.ExecuteAsync(
            new UpdateProductCommand(ProductId.New(), "x", null, CategoryId.New(), Money.FromTomans(1)), CancellationToken.None);
        var gone = await handler.ExecuteAsync(
            new UpdateProductCommand(archived.Id, "x", null, archived.CategoryId, Money.FromTomans(1)), CancellationToken.None);

        Assert.Equal("catalog.product.not-found", missing.Error?.Code);
        Assert.Equal("catalog.product.archived", gone.Error?.Code);
    }

    [Fact]
    public async Task ArchiveTakesTheProductOutOfUseAndDoingItTwiceIsHarmless()
    {
        var context = new ApplicationTestContext();
        var product = Existing(context);
        var handler = new ArchiveProductHandler(context.Products, context.Audit, context.UnitOfWork, context.User, context.Clock);

        var first = await handler.ExecuteAsync(new ArchiveProductCommand(product.Id), CancellationToken.None);
        var second = await handler.ExecuteAsync(new ArchiveProductCommand(product.Id), CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(ProductStatus.Archived, product.Status);
        Assert.Equal(1, context.UnitOfWork.CommitCount); // the second call changed nothing
        Assert.Equal("catalog.product.archived", Assert.Single(context.Audit.Entries).Action);
    }

    [Fact]
    public async Task ArchiveOfAnUnknownProductFails()
    {
        var context = new ApplicationTestContext();

        var result = await new ArchiveProductHandler(context.Products, context.Audit, context.UnitOfWork, context.User, context.Clock)
            .ExecuteAsync(new ArchiveProductCommand(ProductId.New()), CancellationToken.None);

        Assert.Equal("catalog.product.not-found", result.Error?.Code);
    }

    [Fact]
    public async Task ListTrimsTheTermTreatsBlankAsNoTermAndCapsTheRowCount()
    {
        var reader = new RecordingListReader();
        var handler = new ListProductsHandler(reader);

        await handler.ExecuteAsync(new ProductListQuery("  گردو  ", null, Take: 10_000), CancellationToken.None);
        Assert.Equal("گردو", reader.Last!.Term);
        Assert.Equal(ListProductsHandler.MaximumTake, reader.Last.Take);

        await handler.ExecuteAsync(new ProductListQuery("   ", null, Take: 0), CancellationToken.None);
        Assert.Null(reader.Last.Term);
        Assert.Equal(1, reader.Last.Take);
    }

    private sealed class RecordingListReader : IProductListReader
    {
        public ProductListQuery? Last { get; private set; }

        public Task<ProductListPage> ListAsync(ProductListQuery query, CancellationToken cancellationToken)
        {
            Last = query;
            return Task.FromResult(new ProductListPage([], 0, 0));
        }
    }
}
