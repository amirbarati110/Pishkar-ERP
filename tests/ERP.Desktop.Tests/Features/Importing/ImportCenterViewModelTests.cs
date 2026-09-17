using System.Text;
using ERP.Application.Common;
using ERP.Application.Importing;
using ERP.Desktop.Tests.TestDoubles;
using ERP.Domain.Catalog;
using ERP.Domain.Common;
using ERP.Domain.Inventory;
using ERP.Presentation.Features.Importing;

namespace ERP.Desktop.Tests.Features.Importing;

public sealed class ImportCenterViewModelTests
{
    [Fact]
    public async Task AValidFileShowsEveryRowReadyAndAllowsImporting()
    {
        var categoryId = CategoryId.New();
        var unitId = UnitId.New();
        var catalog = new StubCatalogLookupReader(new Application.Catalog.CatalogLookupSnapshot(
            [new Application.Catalog.CategoryLookupItem(categoryId, "مواد غذایی", null, 0)],
            [new Application.Catalog.UnitLookupItem(unitId, "عدد", "عدد", false)],
            []));
        var table = new SpreadsheetTable(
            ["نام کالا", "دسته‌بندی", "واحد", "قیمت فروش (تومان)"],
            [new SpreadsheetRow(2, ["برنج ایرانی", "مواد غذایی", "عدد", "245000"])]);
        var commit = new RecordingCommitHandler();
        var viewModel = new ImportCenterViewModel(new FakeSpreadsheetReader(table), catalog, commit, WarehouseId.New());

        await viewModel.LoadFileAsync("test.xlsx", EmptyStream(), CancellationToken.None);

        Assert.True(viewModel.HasPreview);
        Assert.True(viewModel.CanImport);
        Assert.Null(viewModel.ErrorMessage);
        var row = Assert.Single(viewModel.PreviewRows);
        Assert.False(row.HasError);
        Assert.Contains("برنج ایرانی", row.Text, StringComparison.Ordinal);

        await viewModel.ImportCommand.ExecuteAsync(null);

        Assert.Equal(1, commit.CallCount);
        Assert.NotNull(viewModel.ResultMessage);
        Assert.False(viewModel.HasPreview);
        Assert.False(viewModel.CanImport);
    }

    [Fact]
    public async Task AFileWithAnUnknownCategoryShowsTheErrorAndRefusesToImport()
    {
        var unitId = UnitId.New();
        var catalog = new StubCatalogLookupReader(new Application.Catalog.CatalogLookupSnapshot(
            [],
            [new Application.Catalog.UnitLookupItem(unitId, "عدد", "عدد", false)],
            []));
        var table = new SpreadsheetTable(
            ["نام کالا", "دسته‌بندی", "واحد", "قیمت فروش (تومان)"],
            [new SpreadsheetRow(5, ["چای", "نوشیدنی گرم", "عدد", "150000"])]);
        var commit = new RecordingCommitHandler();
        var viewModel = new ImportCenterViewModel(new FakeSpreadsheetReader(table), catalog, commit, WarehouseId.New());

        await viewModel.LoadFileAsync("test.xlsx", EmptyStream(), CancellationToken.None);

        Assert.True(viewModel.HasPreview);
        Assert.False(viewModel.CanImport);
        var row = Assert.Single(viewModel.PreviewRows);
        Assert.True(row.HasError);
        Assert.Contains("نوشیدنی گرم", row.Text, StringComparison.Ordinal);

        await viewModel.ImportCommand.ExecuteAsync(null);

        Assert.Equal(0, commit.CallCount);
    }

    [Fact]
    public async Task ABrokenFileShowsThePersianReaderErrorWithoutThrowing()
    {
        var catalog = new StubCatalogLookupReader();
        var commit = new RecordingCommitHandler();
        var viewModel = new ImportCenterViewModel(
            new ThrowingSpreadsheetReader("صفحه اول فایل اکسل خالی است."), catalog, commit, WarehouseId.New());

        await viewModel.LoadFileAsync("broken.xlsx", EmptyStream(), CancellationToken.None);

        Assert.False(viewModel.HasPreview);
        Assert.Equal("صفحه اول فایل اکسل خالی است.", viewModel.ErrorMessage);
    }

    private static MemoryStream EmptyStream() => new(Encoding.UTF8.GetBytes("x"));

    private sealed class FakeSpreadsheetReader(SpreadsheetTable table) : ISpreadsheetReader
    {
        public Task<SpreadsheetTable> ReadFirstWorksheetAsync(Stream workbook, CancellationToken cancellationToken) =>
            Task.FromResult(table);
    }

    private sealed class ThrowingSpreadsheetReader(string message) : ISpreadsheetReader
    {
        public Task<SpreadsheetTable> ReadFirstWorksheetAsync(Stream workbook, CancellationToken cancellationToken) =>
            throw new InvalidDataException(message);
    }

    private sealed class RecordingCommitHandler : ICommitProductImportHandler
    {
        public int CallCount { get; private set; }

        public Task<Result<int>> ExecuteAsync(CommitProductImportCommand command, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(Result.Success(command.Rows.Count));
        }
    }
}
