using System.Globalization;
using ERP.Application.Catalog;
using ERP.Domain.Catalog;
using ERP.Domain.Common;

namespace ERP.Application.Importing;

public sealed record PreparedProductImportRow(
    int RowNumber,
    string Name,
    string? Sku,
    CategoryId CategoryId,
    UnitId BaseUnitId,
    Money SalePrice,
    IReadOnlyList<string> Barcodes);

public sealed record ProductImportIssue(
    int? RowNumber,
    string Code,
    string Message);

public sealed record ProductImportPreview(
    IReadOnlyList<PreparedProductImportRow> Items,
    IReadOnlyList<ProductImportIssue> Issues)
{
    public bool CanImport => Items.Count > 0 && Issues.Count == 0;
}

public sealed class ProductImportPreparer
{
    public static ProductImportPreview Prepare(
        SpreadsheetTable table,
        CatalogLookupSnapshot catalog)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(catalog);

        int nameColumn;
        int? skuColumn;
        int? barcodeColumn;
        int categoryColumn;
        int unitColumn;
        int priceColumn;
        try
        {
            nameColumn = FindColumn(table.Headers, "نام کالا");
            skuColumn = FindOptionalColumn(table.Headers, "کد کالا");
            barcodeColumn = FindOptionalColumn(table.Headers, "بارکد");
            categoryColumn = FindColumn(table.Headers, "دسته‌بندی");
            unitColumn = FindColumn(table.Headers, "واحد");
            priceColumn = FindColumn(table.Headers, "قیمت فروش (تومان)");
        }
        catch (InvalidDataException exception)
        {
            return new ProductImportPreview(
                [],
                [new ProductImportIssue(null, "import.product.missing-column", exception.Message)]);
        }

        var items = new List<PreparedProductImportRow>();
        var issues = new List<ProductImportIssue>();
        var seenBarcodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in table.Rows)
        {
            var name = ReadOptional(row, nameColumn);
            if (name is null)
            {
                issues.Add(new ProductImportIssue(
                    row.RowNumber,
                    "import.product.name-required",
                    "نام کالا را وارد کنید."));
                continue;
            }

            var categoryName = ReadOptional(row, categoryColumn);
            if (categoryName is null)
            {
                issues.Add(new ProductImportIssue(
                    row.RowNumber,
                    "import.product.category-required",
                    "دسته‌بندی را وارد کنید."));
                continue;
            }

            var category = catalog.Categories.FirstOrDefault(item =>
                string.Equals(item.Name, categoryName, StringComparison.OrdinalIgnoreCase));
            if (category is null)
            {
                issues.Add(new ProductImportIssue(
                    row.RowNumber,
                    "import.product.category-not-found",
                    $"دسته‌بندی «{categoryName}» پیدا نشد."));
                continue;
            }

            var unitName = ReadOptional(row, unitColumn);
            if (unitName is null)
            {
                issues.Add(new ProductImportIssue(
                    row.RowNumber,
                    "import.product.unit-required",
                    "واحد را وارد کنید."));
                continue;
            }

            var unit = catalog.Units.First(item =>
                string.Equals(item.Name, unitName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.Symbol, unitName, StringComparison.OrdinalIgnoreCase));
            var priceText = ReadOptional(row, priceColumn);
            if (priceText is null
                || !TryParseTomans(priceText, out var salePriceTomans)
                || salePriceTomans < 0)
            {
                issues.Add(new ProductImportIssue(
                    row.RowNumber,
                    "import.product.invalid-price",
                    "قیمت فروش باید یک عدد صحیح و صفر یا بیشتر باشد."));
                continue;
            }

            var barcode = ReadOptional(row, barcodeColumn);
            if (barcode is not null && !seenBarcodes.Add(barcode))
            {
                issues.Add(new ProductImportIssue(
                    row.RowNumber,
                    "import.product.duplicate-barcode-in-file",
                    $"بارکد {barcode} در همین فایل تکرار شده است."));
                continue;
            }

            items.Add(new PreparedProductImportRow(
                row.RowNumber,
                name,
                ReadOptional(row, skuColumn),
                category.Id,
                unit.Id,
                Money.FromTomans(salePriceTomans),
                barcode is null ? [] : [barcode]));
        }

        return new ProductImportPreview(items, issues);
    }

    private static int FindColumn(IReadOnlyList<string> headers, string expected)
    {
        var index = FindOptionalColumn(headers, expected);
        return index ?? throw new InvalidDataException($"ستون «{expected}» در فایل اکسل وجود ندارد.");
    }

    private static int? FindOptionalColumn(IReadOnlyList<string> headers, string expected)
    {
        for (var index = 0; index < headers.Count; index++)
        {
            if (string.Equals(headers[index].Trim(), expected, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return null;
    }

    private static string? ReadOptional(SpreadsheetRow row, int? column)
    {
        if (column is null || column.Value >= row.Cells.Count)
        {
            return null;
        }

        var value = row.Cells[column.Value]?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static bool TryParseTomans(string value, out long result)
    {
        Span<char> buffer = stackalloc char[value.Length];
        var length = 0;
        foreach (var character in value)
        {
            if (character is '٬' or ',' or ' ' or '\u00A0')
            {
                continue;
            }

            buffer[length++] = character switch
            {
                >= '۰' and <= '۹' => (char)('0' + character - '۰'),
                >= '٠' and <= '٩' => (char)('0' + character - '٠'),
                _ => character,
            };
        }

        return long.TryParse(
            buffer[..length],
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out result);
    }
}
