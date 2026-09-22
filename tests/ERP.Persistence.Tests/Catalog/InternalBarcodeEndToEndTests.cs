using ERP.Application.Catalog;
using ERP.Application.Common;
using ERP.Domain.Catalog;
using ERP.Domain.Common;
using ERP.Persistence.Catalog;
using ERP.Persistence.Database;
using ERP.Persistence.Services;
using ERP.Persistence.Tests.TestSupport;

namespace ERP.Persistence.Tests.Catalog;

/// <summary>
/// «ساخت بارکد خودکار» on a real Firebird (checklist «ن-۳»): the sequence never repeats a number,
/// a product without a barcode gets one added, and one that already has a barcode keeps it.
/// </summary>
public sealed class InternalBarcodeEndToEndTests
{
    [Fact]
    public async Task TwoRequestsGetDifferentSerialsAndTheBarcodeIsAttachedOnlyWhenTheProductHasNone()
    {
        await using var database = FirebirdTestDatabase.Create();
        var factory = new FirebirdConnectionFactory(database.Options);
        var defaults = await new FirebirdDatabaseBootstrapper(factory).InitializeAsync(CancellationToken.None);
        var clock = new TestClock { UtcNow = new DateTimeOffset(2026, 9, 22, 8, 0, 0, TimeSpan.Zero) };
        var setup = new FirebirdRetailSetupService(factory, new TestUserContext(Guid.NewGuid()), clock);
        var generate = new GenerateProductBarcodeHandler(
            new FirebirdInternalBarcodeSequenceForTests(factory),
            new FirebirdProductRepositoryForTests(factory));

        var category = (await setup.ExecuteAsync(new CreateCategoryCommand("مواد غذایی", null, 1), CancellationToken.None)).Value;
        var withBarcode = (await setup.ExecuteAsync(
            new CreateProductCommand("برنج", "RICE-1", category, defaults.EachUnitId, Money.FromTomans(245_000), ["6260000001111"]),
            CancellationToken.None)).Value;
        var withoutBarcode = (await setup.ExecuteAsync(
            new CreateProductCommand("گردو", "NUT-1", category, defaults.EachUnitId, Money.FromTomans(420_000), []),
            CancellationToken.None)).Value;

        // two calls never hand out the same number
        var first = await generate.ExecuteAsync(CancellationToken.None);
        var second = await generate.ExecuteAsync(CancellationToken.None);
        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.NotEqual(first.Value, second.Value);
        Assert.True(InternalBarcode.IsInternal(first.Value));

        // attaching it to a product that has none works
        var attach = await setup.ExecuteAsync(
            new UpdateProductCommand(withoutBarcode, "گردو", "NUT-1", category, Money.FromTomans(420_000), first.Value),
            CancellationToken.None);
        Assert.True(attach.IsSuccess, attach.Error?.Message);

        // a product that already has a barcode refuses a new one — the old one is not replaced
        var refused = await setup.ExecuteAsync(
            new UpdateProductCommand(withBarcode, "برنج", "RICE-1", category, Money.FromTomans(245_000), second.Value),
            CancellationToken.None);
        Assert.Equal("catalog.product.has-barcode", refused.Error?.Code);

        var reader = new FirebirdProductRepositoryForTests(factory);
        Assert.True(await reader.BarcodeExistsAsync(first.Value!, CancellationToken.None));
        Assert.False(await reader.BarcodeExistsAsync(second.Value!, CancellationToken.None));
    }

    // Thin wrappers so the Application-layer handler (which needs its own unit of work) can be
    // exercised the same way the real app composes it in FirebirdRetailSetupService.
    private sealed class FirebirdInternalBarcodeSequenceForTests(FirebirdConnectionFactory factory) : IInternalBarcodeSequence
    {
        public async Task<long> NextAsync(CancellationToken cancellationToken)
        {
            await using var unitOfWork = await FirebirdUnitOfWork.CreateAsync(factory, cancellationToken);
            var value = await new FirebirdInternalBarcodeSequence(unitOfWork).NextAsync(cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
            return value;
        }
    }

    private sealed class FirebirdProductRepositoryForTests(FirebirdConnectionFactory factory) : IProductRepository
    {
        public async Task<Product?> GetByIdAsync(ProductId productId, CancellationToken cancellationToken)
        {
            await using var unitOfWork = await FirebirdUnitOfWork.CreateAsync(factory, cancellationToken);
            return await new FirebirdProductRepository(unitOfWork).GetByIdAsync(productId, cancellationToken);
        }

        public async Task<bool> BarcodeExistsAsync(string barcode, CancellationToken cancellationToken)
        {
            await using var unitOfWork = await FirebirdUnitOfWork.CreateAsync(factory, cancellationToken);
            return await new FirebirdProductRepository(unitOfWork).BarcodeExistsAsync(barcode, cancellationToken);
        }

        public Task AddAsync(Product product, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task AddBarcodeAsync(ProductId productId, string barcode, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task UpdateAsync(Product product, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed record TestUserContext(Guid UserId) : IUserContext;

    private sealed class TestClock : IClock
    {
        public required DateTimeOffset UtcNow { get; set; }
    }
}
