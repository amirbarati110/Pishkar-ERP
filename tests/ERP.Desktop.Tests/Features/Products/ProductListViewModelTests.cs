using ERP.Application.Catalog;
using ERP.Application.Common;
using ERP.Domain.Catalog;
using ERP.Domain.Common;
using ERP.Presentation.Features.Products;

namespace ERP.Desktop.Tests.Features.Products;

public sealed class ProductListViewModelTests
{
    private static readonly UnitId Unit = UnitId.New();
    private static readonly CategoryId Food = CategoryId.New();
    private static readonly CategoryId Dried = CategoryId.New();
    private static readonly CategoryId Walnut = CategoryId.New();
    private static readonly CategoryId Drinks = CategoryId.New();

    private static ProductListRow Row(string name, decimal stock = 3, CategoryId? category = null) =>
        new(ProductId.New(), "sku-1", name, category ?? Food, "مواد غذایی", Unit, "عدد", Money.FromTomans(245_000), stock, null);

    private static (ProductListViewModel Vm, FakeList List, FakeArchive Archive) Create(params ProductListRow[] rows)
    {
        var list = new FakeList { Rows = [.. rows] };
        var archive = new FakeArchive();
        var vm = new ProductListViewModel(list, archive, new FakeLookup()) { SearchDelay = TimeSpan.Zero };
        return (vm, list, archive);
    }

    [Fact]
    public async Task LoadShowsTheGroupTreeInOrderWithDepthAndTheListWithTotals()
    {
        var (vm, _, _) = Create(Row("برنج"), Row("گردو", stock: 0));

        await vm.LoadAsync(CancellationToken.None);

        Assert.Equal(
            ["همه گروه‌ها", "مواد غذایی", "خشکبار", "گردو", "نوشیدنی"],
            vm.Groups.Select(group => group.Name));
        Assert.Equal([0, 0, 1, 2, 0], vm.Groups.Select(group => group.Depth));
        Assert.Equal(2, vm.Items.Count);
        Assert.Equal("۲", vm.CountText);
        Assert.Equal("۳", vm.StockTotalText);
        Assert.Equal("۲۴۵٬۰۰۰", vm.Items[0].PriceText.Replace(",", "٬"));
        Assert.True(vm.Items[1].IsOutOfStock);
        Assert.False(vm.IsEmpty);
        Assert.Equal(string.Empty, vm.TruncatedText);
    }

    [Fact]
    public async Task TypingASearchTermRereadsTheListWithThatTerm()
    {
        var (vm, list, _) = Create(Row("برنج"));
        await vm.LoadAsync(CancellationToken.None);

        vm.SearchText = "برن";

        Assert.Equal("برن", list.LastQuery!.Term);
        Assert.Null(list.LastQuery.CategoryId);
    }

    [Fact]
    public async Task PickingAGroupFiltersByThatCategory()
    {
        var (vm, list, _) = Create(Row("گردو"));
        await vm.LoadAsync(CancellationToken.None);

        vm.SelectedGroup = vm.Groups.Single(group => group.Name == "خشکبار");

        Assert.Equal(Dried, list.LastQuery!.CategoryId);
    }

    [Fact]
    public async Task AnEmptyResultIsFlaggedSoThePageCanSaySo()
    {
        var (vm, _, _) = Create();

        await vm.LoadAsync(CancellationToken.None);

        Assert.True(vm.IsEmpty);
        Assert.Equal("۰", vm.CountText);
    }

    [Fact]
    public async Task WhenMoreMatchThanAreShownTheScreenSaysSo()
    {
        var (vm, list, _) = Create(Row("الف"));
        list.TotalCountOverride = 900;

        await vm.LoadAsync(CancellationToken.None);

        Assert.Contains("۹۰۰", vm.TruncatedText);
        Assert.Contains("جست‌وجو را دقیق‌تر", vm.TruncatedText);
    }

    [Fact]
    public async Task DeleteAsksFirstAndNothingIsArchivedUntilConfirmed()
    {
        var (vm, _, archive) = Create(Row("برنج"));
        await vm.LoadAsync(CancellationToken.None);
        Assert.False(vm.ArchiveCommand.CanExecute(null)); // nothing selected

        vm.SelectedItem = vm.Items[0];
        Assert.True(vm.ArchiveCommand.CanExecute(null));
        vm.ArchiveCommand.Execute(null);

        Assert.True(vm.IsArchiveConfirmOpen);
        Assert.Contains("برنج", vm.ArchiveConfirmText);
        Assert.Empty(archive.Archived);

        vm.CancelArchiveCommand.Execute(null);
        Assert.False(vm.IsArchiveConfirmOpen);
        Assert.Empty(archive.Archived);
    }

    [Fact]
    public async Task ConfirmedDeleteArchivesTheSelectedProductAndRefreshesTheList()
    {
        var rice = Row("برنج");
        var (vm, list, archive) = Create(rice, Row("گردو"));
        await vm.LoadAsync(CancellationToken.None);
        vm.SelectedItem = vm.Items[0];
        vm.ArchiveCommand.Execute(null);
        list.Rows = [.. list.Rows.Skip(1)]; // the store no longer lists it

        await vm.ConfirmArchiveCommand.ExecuteAsync(null);

        Assert.Equal([rice.Id], archive.Archived);
        Assert.False(vm.IsArchiveConfirmOpen);
        Assert.Single(vm.Items);
        Assert.Contains("حذف شد", vm.Notice);
        Assert.Null(vm.SelectedItem);
    }

    [Fact]
    public async Task AFailedDeleteShowsTheReasonAndKeepsTheList()
    {
        var (vm, _, archive) = Create(Row("برنج"));
        archive.Failure = "این کالا پیدا نشد.";
        await vm.LoadAsync(CancellationToken.None);
        vm.SelectedItem = vm.Items[0];
        vm.ArchiveCommand.Execute(null);

        await vm.ConfirmArchiveCommand.ExecuteAsync(null);

        Assert.Equal("این کالا پیدا نشد.", vm.ErrorMessage);
        Assert.Single(vm.Items);
    }

    private sealed class FakeList : IListProductsHandler
    {
        public List<ProductListRow> Rows { get; set; } = [];

        public ProductListQuery? LastQuery { get; private set; }

        public int? TotalCountOverride { get; set; }

        public Task<ProductListPage> ExecuteAsync(ProductListQuery query, CancellationToken cancellationToken)
        {
            LastQuery = query;
            return Task.FromResult(new ProductListPage(Rows, TotalCountOverride ?? Rows.Count, Rows.Sum(row => row.Stock)));
        }
    }

    private sealed class FakeArchive : IArchiveProductHandler
    {
        public List<ProductId> Archived { get; } = [];

        public string? Failure { get; set; }

        public Task<Result<bool>> ExecuteAsync(ArchiveProductCommand command, CancellationToken cancellationToken)
        {
            if (Failure is not null)
            {
                return Task.FromResult(Result.Failure<bool>("catalog.product.not-found", Failure));
            }

            Archived.Add(command.ProductId);
            return Task.FromResult(Result.Success(true));
        }
    }

    private sealed class FakeLookup : ICatalogLookupReader
    {
        public Task<CatalogLookupSnapshot> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new CatalogLookupSnapshot(
                [
                    new CategoryLookupItem(Drinks, "نوشیدنی", null, 2),
                    new CategoryLookupItem(Walnut, "گردو", Dried, 1),
                    new CategoryLookupItem(Dried, "خشکبار", Food, 1),
                    new CategoryLookupItem(Food, "مواد غذایی", null, 1),
                ],
                [],
                []));
    }
}
