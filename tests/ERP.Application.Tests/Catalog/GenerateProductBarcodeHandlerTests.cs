using ERP.Application.Catalog;
using ERP.Domain.Catalog;
using ERP.Domain.Common;

namespace ERP.Application.Tests.Catalog;

/// <summary>«ساخت بارکد خودکار» (checklist «ن-۳»): the handler behind the button on the product form.</summary>
public sealed class GenerateProductBarcodeHandlerTests
{
    [Fact]
    public async Task ANewBarcodeIsMadeFromTheNextSerial()
    {
        var handler = new GenerateProductBarcodeHandler(new FakeSequence(), new FakeProducts());

        var result = await handler.ExecuteAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(InternalBarcode.FromSerial(1), result.Value);
    }

    [Fact]
    public async Task ASerialAlreadyUsedByHandIsSkipped()
    {
        var products = new FakeProducts { Existing = { InternalBarcode.FromSerial(1), InternalBarcode.FromSerial(2) } };
        var handler = new GenerateProductBarcodeHandler(new FakeSequence(), products);

        var result = await handler.ExecuteAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(InternalBarcode.FromSerial(3), result.Value);
    }

    [Fact]
    public async Task RunningOutOfSerialsFailsHonestlyInsteadOfLooping()
    {
        var sequence = new FakeSequence { Next = InternalBarcode.MaximumSerial + 1 };
        var handler = new GenerateProductBarcodeHandler(sequence, new FakeProducts());

        var result = await handler.ExecuteAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("catalog.barcode.exhausted", result.Error?.Code);
    }

    private sealed class FakeSequence : IInternalBarcodeSequence
    {
        public long Next { get; set; } = 1;

        public Task<long> NextAsync(CancellationToken cancellationToken) => Task.FromResult(Next++);
    }

    private sealed class FakeProducts : IProductRepository
    {
        public HashSet<string> Existing { get; } = [];

        public Task<Product?> GetByIdAsync(ProductId productId, CancellationToken cancellationToken) =>
            Task.FromResult<Product?>(null);

        public Task<bool> BarcodeExistsAsync(string barcode, CancellationToken cancellationToken) =>
            Task.FromResult(Existing.Contains(barcode));

        public Task AddAsync(Product product, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task AddBarcodeAsync(ProductId productId, string barcode, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task UpdateAsync(Product product, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
