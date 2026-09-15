using System.Globalization;
using System.IO.Compression;
using System.Text;
using ERP.Infrastructure.Spreadsheets;

namespace ERP.Infrastructure.Tests.Spreadsheets;

public sealed class OpenXmlSpreadsheetReaderTests
{
    [Fact]
    public async Task ReadFirstWorksheetReturnsPersianHeadersAndCellValues()
    {
        await using var workbook = CreateWorkbook(
            [
                ["نام کالا", "بارکد", "قیمت فروش (تومان)"],
                ["برنج ایرانی", "6260001000011", "245000"],
            ]);
        var reader = new OpenXmlSpreadsheetReader();
        var table = await reader.ReadFirstWorksheetAsync(workbook, CancellationToken.None);

        Assert.Equal(["نام کالا", "بارکد", "قیمت فروش (تومان)"], table.Headers);
        var row = Assert.Single(table.Rows);
        Assert.Equal(2, row.RowNumber);
        Assert.Equal("برنج ایرانی", row.Cells[0]);
        Assert.Equal("245000", row.Cells[2]);
    }

    private static MemoryStream CreateWorkbook(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            WriteEntry(
                archive,
                "[Content_Types].xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                </Types>
                """);
            WriteEntry(
                archive,
                "_rels/.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """);
            WriteEntry(
                archive,
                "xl/workbook.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets><sheet name="کالاها" sheetId="1" r:id="rId1"/></sheets>
                </workbook>
                """);
            WriteEntry(
                archive,
                "xl/_rels/workbook.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                </Relationships>
                """);

            var sheet = new StringBuilder(
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
            for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                sheet.Append(CultureInfo.InvariantCulture, $"<row r=\"{rowIndex + 1}\">");
                for (var columnIndex = 0; columnIndex < rows[rowIndex].Count; columnIndex++)
                {
                    var cellReference = $"{GetColumnName(columnIndex)}{rowIndex + 1}";
                    sheet.Append(
                        CultureInfo.InvariantCulture,
                        $"<c r=\"{cellReference}\" t=\"inlineStr\"><is><t>");
                    sheet.Append(System.Security.SecurityElement.Escape(rows[rowIndex][columnIndex]));
                    sheet.Append("</t></is></c>");
                }

                sheet.Append("</row>");
            }

            sheet.Append("</sheetData></worksheet>");
            WriteEntry(archive, "xl/worksheets/sheet1.xml", sheet.ToString());
        }

        stream.Position = 0;
        return stream;
    }

    private static string GetColumnName(int zeroBasedIndex)
    {
        return ((char)('A' + zeroBasedIndex)).ToString();
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}
