using System.IO.Compression;
using System.Text;
using ClosedXML.Excel;
using Delegat.Infrastructure.Middleware;
using Delegat.Services;

namespace Delegat.Tests;

public class ExcelProductImportLimitsTests
{
    private const string SheetHeader =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>";
    private const string SheetFooter = "</sheetData></worksheet>";

    [Fact]
    public void ParseImportFile_ReadsRegularWorkbook()
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Products");
        ws.Cell(1, 1).Value = "id";
        ws.Cell(1, 2).Value = "name";
        ws.Cell(2, 1).Value = "ID";
        ws.Cell(2, 2).Value = "Name";
        ws.Cell(3, 1).Value = "42";
        ws.Cell(3, 2).Value = "Chair";
        using var stream = new MemoryStream();
        wb.SaveAs(stream);
        stream.Position = 0;

        var parsed = new ExcelProductService().ParseImportFile(stream);

        var row = Assert.Single(parsed.Rows);
        Assert.Equal(42, row.ProductId);
        Assert.Equal("Chair", row.Fields["name"]);
    }

    [Fact]
    public void ParseImportFile_RejectsSheetThatExpandsPastByteCap()
    {
        var padding = new string(' ', (int)ExcelProductService.MaxImportExpandedBytes + 1024);
        using var stream = BuildArchive(("xl/worksheets/sheet1.xml", SheetHeader + padding + SheetFooter));
        Assert.True(stream.Length < ExcelProductService.MaxImportUploadBytes);

        AssertRejected(stream, "excel_import_too_large");
    }

    [Fact]
    public void ParseImportFile_RejectsSheetWithTooManyCells()
    {
        var sheet = new StringBuilder(SheetHeader).Append("<row r=\"1\">");
        for (var i = 0; i <= ExcelProductService.MaxImportCells; i++)
            sheet.Append("<c/>");
        sheet.Append("</row>").Append(SheetFooter);
        using var stream = BuildArchive(("xl/worksheets/sheet1.xml", sheet.ToString()));

        AssertRejected(stream, "excel_import_too_large");
    }

    [Fact]
    public void ParseImportFile_RejectsArchiveWithTooManyEntries()
    {
        var entries = Enumerable.Range(0, ExcelProductService.MaxImportArchiveEntries + 1)
            .Select(i => ($"xl/media/{i}.bin", "x"))
            .ToArray();
        using var stream = BuildArchive(entries);

        AssertRejected(stream, "excel_import_too_large");
    }

    [Fact]
    public void ParseImportFile_RejectsSparseCellBeyondColumnCap()
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Products");
        ws.Cell(1, 1).Value = "id";
        ws.Cell(3, ExcelProductService.MaxImportColumns + 1).Value = "x";
        using var stream = new MemoryStream();
        wb.SaveAs(stream);
        stream.Position = 0;

        AssertRejected(stream, "excel_import_too_large");
    }

    [Fact]
    public void ParseImportFile_RejectsNonZipUpload()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("id,name\n1,Chair"));

        AssertRejected(stream, "excel_import_invalid");
    }

    private static void AssertRejected(Stream stream, string code)
    {
        var ex = Assert.Throws<ApiException>(() => new ExcelProductService().ParseImportFile(stream));
        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(code, ex.Code);
    }

    private static MemoryStream BuildArchive(params (string Name, string Content)[] entries)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var writer = new StreamWriter(archive.CreateEntry(name, CompressionLevel.Optimal).Open());
                writer.Write(content);
            }
        }
        stream.Position = 0;
        return stream;
    }
}
