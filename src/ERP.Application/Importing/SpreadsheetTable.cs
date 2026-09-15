namespace ERP.Application.Importing;

public sealed record SpreadsheetRow(
    int RowNumber,
    IReadOnlyList<string?> Cells);

public sealed record SpreadsheetTable(
    IReadOnlyList<string> Headers,
    IReadOnlyList<SpreadsheetRow> Rows);

public interface ISpreadsheetReader
{
    Task<SpreadsheetTable> ReadFirstWorksheetAsync(
        Stream workbook,
        CancellationToken cancellationToken);
}
