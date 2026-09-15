using ERP.Application.Catalog;

namespace ERP.Application.Tests.Catalog;

public sealed class SearchProductsTests
{
    [Fact]
    public async Task ExecuteAsyncReturnsEmptyForBlankTermWithoutCallingReader()
    {
        var reader = new RecordingProductSearchReader();
        var handler = new SearchProductsHandler(reader);

        var results = await handler.ExecuteAsync(
            new SearchProductsQuery("   "),
            CancellationToken.None);

        Assert.Empty(results);
        Assert.Null(reader.LastQuery);
    }

    [Fact]
    public async Task ExecuteAsyncTrimsTermBeforeDelegatingToReader()
    {
        var reader = new RecordingProductSearchReader();
        var handler = new SearchProductsHandler(reader);

        await handler.ExecuteAsync(new SearchProductsQuery("  روغ  "), CancellationToken.None);

        Assert.Equal("روغ", reader.LastQuery?.Term);
    }

    [Fact]
    public async Task ExecuteAsyncUsesDefaultMaxResultsWhenNonPositive()
    {
        var reader = new RecordingProductSearchReader();
        var handler = new SearchProductsHandler(reader);

        await handler.ExecuteAsync(new SearchProductsQuery("روغن", MaxResults: 0), CancellationToken.None);

        Assert.Equal(8, reader.LastQuery?.MaxResults);
    }

    [Fact]
    public async Task ExecuteAsyncClampsMaxResultsToHardCap()
    {
        var reader = new RecordingProductSearchReader();
        var handler = new SearchProductsHandler(reader);

        await handler.ExecuteAsync(new SearchProductsQuery("روغن", MaxResults: 5_000), CancellationToken.None);

        Assert.Equal(50, reader.LastQuery?.MaxResults);
    }

    private sealed class RecordingProductSearchReader : IProductSearchReader
    {
        public SearchProductsQuery? LastQuery { get; private set; }

        public Task<IReadOnlyList<ProductSearchResult>> SearchAsync(
            SearchProductsQuery query,
            CancellationToken cancellationToken)
        {
            LastQuery = query;
            return Task.FromResult<IReadOnlyList<ProductSearchResult>>([]);
        }
    }
}
