using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ERP.Application.Catalog;
using ERP.Application.Importing;
using ERP.Domain.Common;
using ERP.Domain.Inventory;
using ERP.Presentation.Features.Sales;

namespace ERP.Presentation.Features.Importing;

/// <summary>One line of the preview grid — either a ready-to-import row or a validation error, sorted together by row number so the cashier reads the file top to bottom.</summary>
public sealed record ImportPreviewRow(int? RowNumber, string Text, bool HasError)
{
    public string RowNumberText => RowNumber is { } number ? PersianNumber.FormatGrouped(number) : "—";
}

/// <summary>
/// «Import Center برای کالا، بارکد، قیمت و موجودی اولیه» (milestone-1 build
/// order step 7). Two steps only: pick a file → see a full Persian preview
/// (every row, valid or not) → import, all-or-nothing (§15 acceptance
/// criterion 4). Reading the file (<see cref="ISpreadsheetReader"/>) and
/// preparing it (<see cref="ProductImportPreparer"/>) never touch the
/// database — only <see cref="ImportAsync"/> does, once, in one transaction.
/// </summary>
public sealed partial class ImportCenterViewModel : ObservableObject
{
    private readonly ISpreadsheetReader _spreadsheetReader;
    private readonly ICatalogLookupReader _catalogLookup;
    private readonly ICommitProductImportHandler _commit;
    private readonly WarehouseId _warehouseId;

    private IReadOnlyList<PreparedProductImportRow> _validRows = [];

    public ImportCenterViewModel(
        ISpreadsheetReader spreadsheetReader,
        ICatalogLookupReader catalogLookup,
        ICommitProductImportHandler commit,
        WarehouseId warehouseId)
    {
        _spreadsheetReader = spreadsheetReader;
        _catalogLookup = catalogLookup;
        _commit = commit;
        _warehouseId = warehouseId;
    }

    public ObservableCollection<ImportPreviewRow> PreviewRows { get; } = [];

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? FileName { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial string? ResultMessage { get; set; }

    [ObservableProperty]
    public partial bool HasPreview { get; set; }

    [ObservableProperty]
    public partial bool CanImport { get; set; }

    [ObservableProperty]
    public partial string SummaryText { get; set; } = string.Empty;

    public async Task LoadFileAsync(string fileName, Stream fileStream, CancellationToken cancellationToken)
    {
        ErrorMessage = null;
        ResultMessage = null;
        HasPreview = false;
        CanImport = false;
        PreviewRows.Clear();
        IsBusy = true;
        try
        {
            FileName = fileName;
            var table = await _spreadsheetReader.ReadFirstWorksheetAsync(fileStream, cancellationToken).ConfigureAwait(true);
            var catalog = await _catalogLookup.LoadAsync(cancellationToken).ConfigureAwait(true);
            var preview = ProductImportPreparer.Prepare(table, catalog);

            _validRows = preview.Items;

            foreach (var issue in preview.Issues.OrderBy(issue => issue.RowNumber ?? int.MinValue))
            {
                PreviewRows.Add(new ImportPreviewRow(issue.RowNumber, issue.Message, HasError: true));
            }

            foreach (var item in preview.Items.OrderBy(item => item.RowNumber))
            {
                PreviewRows.Add(new ImportPreviewRow(item.RowNumber, Describe(item), HasError: false));
            }

            HasPreview = true;
            CanImport = preview.CanImport;
            SummaryText = preview.Issues.Count == 0
                ? $"{PersianNumber.FormatGrouped(preview.Items.Count)} کالا آماده‌ی وارد کردن است."
                : $"{PersianNumber.FormatGrouped(preview.Issues.Count)} خطا در فایل پیدا شد — تا رفع نشوند، وارد کردن ممکن نیست.";
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            // §15.20: پیام روشن فارسی به کاربر، نه جزئیات فنی فایل خراب.
            ErrorMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        if (!CanImport || _validRows.Count == 0 || IsBusy)
        {
            return;
        }

        ErrorMessage = null;
        IsBusy = true;
        try
        {
            var result = await _commit.ExecuteAsync(
                new CommitProductImportCommand(_validRows, _warehouseId), CancellationToken.None).ConfigureAwait(true);
            if (!result.IsSuccess)
            {
                ErrorMessage = result.Error?.Message ?? "وارد کردن فایل ناموفق بود.";
                return;
            }

            ResultMessage = $"{PersianNumber.FormatGrouped(result.Value)} کالا با موفقیت وارد شد.";
            PreviewRows.Clear();
            _validRows = [];
            HasPreview = false;
            CanImport = false;
            FileName = null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string Describe(PreparedProductImportRow row)
    {
        var priceText = $"{SalesText.Tomans(row.SalePrice)} تومان";
        var openingStockText = row.OpeningStockQuantity is { } quantity
            ? $" · موجودی اولیه {SalesText.Quantity(quantity)}"
            : string.Empty;
        var skuText = string.IsNullOrWhiteSpace(row.Sku) ? string.Empty : $" ({row.Sku})";

        return $"{row.Name}{skuText} — {priceText}{openingStockText}";
    }
}
