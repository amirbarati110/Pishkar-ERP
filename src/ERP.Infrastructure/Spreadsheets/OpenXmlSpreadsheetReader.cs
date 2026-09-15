using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using ERP.Application.Importing;

namespace ERP.Infrastructure.Spreadsheets;

public sealed class OpenXmlSpreadsheetReader : ISpreadsheetReader
{
    private const long MaximumWorksheetBytes = 64 * 1024 * 1024;
    private static readonly XNamespace SpreadsheetNamespace =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace OfficeRelationshipNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelationshipNamespace =
        "http://schemas.openxmlformats.org/package/2006/relationships";

    public async Task<SpreadsheetTable> ReadFirstWorksheetAsync(
        Stream workbook,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        if (!workbook.CanRead)
        {
            throw new ArgumentException("فایل اکسل قابل خواندن نیست.", nameof(workbook));
        }

        using var archive = new ZipArchive(workbook, ZipArchiveMode.Read, true);
        var worksheetEntry = await FindFirstWorksheetAsync(archive, cancellationToken)
            .ConfigureAwait(false);
        if (worksheetEntry.Length > MaximumWorksheetBytes)
        {
            throw new InvalidDataException("حجم صفحه اکسل بیشتر از حد مجاز است.");
        }

        var sharedStrings = await ReadSharedStringsAsync(archive, cancellationToken)
            .ConfigureAwait(false);
        var worksheet = await LoadDocumentAsync(worksheetEntry, cancellationToken)
            .ConfigureAwait(false);

        return ReadTable(worksheet, sharedStrings);
    }

    private static SpreadsheetTable ReadTable(
        XDocument worksheet,
        IReadOnlyList<string> sharedStrings)
    {
        var sourceRows = worksheet
            .Descendants(SpreadsheetNamespace + "row")
            .Select(ReadRow)
            .Where(row => row.Cells.Count > 0)
            .ToArray();
        if (sourceRows.Length == 0)
        {
            throw new InvalidDataException("صفحه اول فایل اکسل خالی است.");
        }

        var headerSource = sourceRows[0];
        var columnCount = headerSource.Cells.Keys.DefaultIfEmpty(-1).Max() + 1;
        var headers = Enumerable.Range(0, columnCount)
            .Select(index => GetCellText(headerSource.Cells.GetValueOrDefault(index), sharedStrings).Trim())
            .ToArray();
        if (headers.All(string.IsNullOrWhiteSpace))
        {
            throw new InvalidDataException("ردیف عنوان ستون‌های فایل اکسل خالی است.");
        }

        var rows = new List<SpreadsheetRow>();
        foreach (var sourceRow in sourceRows.Skip(1))
        {
            var cells = Enumerable.Range(0, headers.Length)
                .Select(index => NullIfEmpty(GetCellText(sourceRow.Cells.GetValueOrDefault(index), sharedStrings)))
                .ToArray();
            if (cells.Any(value => value is not null))
            {
                rows.Add(new SpreadsheetRow(sourceRow.RowNumber, cells));
            }
        }

        return new SpreadsheetTable(headers, rows);
    }

    private static SourceRow ReadRow(XElement row)
    {
        var rowNumber = int.TryParse(
            (string?)row.Attribute("r"),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var parsedRowNumber)
            ? parsedRowNumber
            : 0;
        var cells = new Dictionary<int, SourceCell>();
        var fallbackIndex = 0;

        foreach (var cell in row.Elements(SpreadsheetNamespace + "c"))
        {
            var reference = (string?)cell.Attribute("r");
            var columnIndex = reference is null ? fallbackIndex : GetColumnIndex(reference);
            fallbackIndex = columnIndex + 1;
            cells[columnIndex] = new SourceCell(
                (string?)cell.Attribute("t"),
                string.Concat(cell.Descendants(SpreadsheetNamespace + "t").Select(value => value.Value)),
                cell.Element(SpreadsheetNamespace + "v")?.Value);
        }

        return new SourceRow(rowNumber, cells);
    }

    private static string GetCellText(SourceCell? cell, IReadOnlyList<string> sharedStrings)
    {
        if (cell is null)
        {
            return string.Empty;
        }

        if (cell.Type == "inlineStr")
        {
            return cell.InlineText;
        }

        if (cell.Type == "s"
            && int.TryParse(cell.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
            && index >= 0
            && index < sharedStrings.Count)
        {
            return sharedStrings[index];
        }

        return cell.Value ?? string.Empty;
    }

    private static int GetColumnIndex(string cellReference)
    {
        var result = 0;
        var letterCount = 0;
        foreach (var character in cellReference)
        {
            if (character is < 'A' or > 'Z')
            {
                break;
            }

            result = checked((result * 26) + (character - 'A' + 1));
            letterCount++;
        }

        if (letterCount == 0)
        {
            throw new InvalidDataException($"آدرس سلول اکسل نامعتبر است: {cellReference}");
        }

        return result - 1;
    }

    private static async Task<ZipArchiveEntry> FindFirstWorksheetAsync(
        ZipArchive archive,
        CancellationToken cancellationToken)
    {
        var workbookEntry = GetRequiredEntry(archive, "xl/workbook.xml");
        var workbookDocument = await LoadDocumentAsync(workbookEntry, cancellationToken)
            .ConfigureAwait(false);
        var relationshipId = workbookDocument
            .Descendants(SpreadsheetNamespace + "sheet")
            .Select(sheet => (string?)sheet.Attribute(OfficeRelationshipNamespace + "id"))
            .FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));
        if (relationshipId is null)
        {
            throw new InvalidDataException("فایل اکسل هیچ صفحه‌ای ندارد.");
        }

        var relationshipsEntry = GetRequiredEntry(archive, "xl/_rels/workbook.xml.rels");
        var relationshipsDocument = await LoadDocumentAsync(relationshipsEntry, cancellationToken)
            .ConfigureAwait(false);
        var target = relationshipsDocument
            .Descendants(PackageRelationshipNamespace + "Relationship")
            .Where(item => string.Equals((string?)item.Attribute("Id"), relationshipId, StringComparison.Ordinal))
            .Select(item => (string?)item.Attribute("Target"))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        if (target is null)
        {
            throw new InvalidDataException("صفحه اول فایل اکسل پیدا نشد.");
        }

        var worksheetPath = target.StartsWith('/')
            ? target.TrimStart('/')
            : $"xl/{target}";
        return GetRequiredEntry(archive, worksheetPath.Replace('\\', '/'));
    }

    private static async Task<IReadOnlyList<string>> ReadSharedStringsAsync(
        ZipArchive archive,
        CancellationToken cancellationToken)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
        {
            return [];
        }

        var document = await LoadDocumentAsync(entry, cancellationToken).ConfigureAwait(false);
        return document
            .Descendants(SpreadsheetNamespace + "si")
            .Select(item => string.Concat(item.Descendants(SpreadsheetNamespace + "t").Select(text => text.Value)))
            .ToArray();
    }

    private static ZipArchiveEntry GetRequiredEntry(ZipArchive archive, string path)
    {
        return archive.GetEntry(path)
            ?? throw new InvalidDataException("ساختار فایل اکسل معتبر نیست.");
    }

    private static async Task<XDocument> LoadDocumentAsync(
        ZipArchiveEntry entry,
        CancellationToken cancellationToken)
    {
        await using var stream = entry.Open();
        return await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken)
            .ConfigureAwait(false);
    }

    private static string? NullIfEmpty(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private sealed record SourceRow(int RowNumber, IReadOnlyDictionary<int, SourceCell> Cells);

    private sealed record SourceCell(string? Type, string InlineText, string? Value);
}
