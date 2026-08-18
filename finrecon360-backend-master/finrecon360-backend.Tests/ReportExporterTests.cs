using System.Text;
using ClosedXML.Excel;
using finrecon360_backend.Services.Export;
using Xunit;

namespace finrecon360_backend.Tests;

public class ReportExporterTests
{
    private record SampleRow(string Name, string Note);

    private static readonly IReadOnlyList<ExportColumn<SampleRow>> Columns = new List<ExportColumn<SampleRow>>
    {
        new("Name", r => r.Name),
        new("Note", r => r.Note),
    };

    [Theory]
    [InlineData(null, ReportExportFormat.Csv, true)]
    [InlineData("csv", ReportExportFormat.Csv, true)]
    [InlineData("CSV", ReportExportFormat.Csv, true)]
    [InlineData("xlsx", ReportExportFormat.Xlsx, true)]
    [InlineData("XLSX", ReportExportFormat.Xlsx, true)]
    [InlineData("pdf", ReportExportFormat.Csv, false)]
    public void TryParseFormat_recognizes_supported_formats(string? input, ReportExportFormat expected, bool expectedSuccess)
    {
        var exporter = new ReportExporter();

        var success = exporter.TryParseFormat(input, out var parsed);

        Assert.Equal(expectedSuccess, success);
        Assert.Equal(expected, parsed);
    }

    [Fact]
    public void Export_csv_escapes_commas_quotes_and_newlines()
    {
        var exporter = new ReportExporter();
        var rows = new[]
        {
            new SampleRow("Simple", "plain"),
            new SampleRow("Has, comma", "Has \"quotes\""),
            new SampleRow("Has\nnewline", "trailing"),
        };

        var file = exporter.Export(rows, Columns, "Sample", ReportExportFormat.Csv);
        var text = Encoding.UTF8.GetString(file.Content).TrimStart('﻿');

        Assert.Equal("text/csv", file.ContentType);
        Assert.Equal("csv", file.FileExtension);

        // Row-terminator newlines are \r\n; the embedded \n inside the quoted field has no
        // preceding \r, so a naive \n split still shows it as a distinct (5th) segment even
        // though it's part of the same quoted CSV field, not a new row.
        var lines = text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
        Assert.Equal("Name,Note", lines[0]);
        Assert.Equal("Simple,plain", lines[1]);
        Assert.Equal("\"Has, comma\",\"Has \"\"quotes\"\"\"", lines[2]);
        Assert.Equal(5, lines.Length);
        Assert.Equal("\"Has", lines[3]);
        Assert.Equal("newline\",trailing", lines[4]);
    }

    [Fact]
    public void Export_csv_empty_rows_still_writes_header()
    {
        var exporter = new ReportExporter();

        var file = exporter.Export(Array.Empty<SampleRow>(), Columns, "Sample", ReportExportFormat.Csv);
        var text = Encoding.UTF8.GetString(file.Content).TrimStart('﻿').Trim();

        Assert.Equal("Name,Note", text);
    }

    [Fact]
    public void Export_xlsx_writes_header_and_rows()
    {
        var exporter = new ReportExporter();
        var rows = new[]
        {
            new SampleRow("Row One", "First"),
            new SampleRow("Row Two", "Second"),
        };

        var file = exporter.Export(rows, Columns, "Sample Sheet", ReportExportFormat.Xlsx);

        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", file.ContentType);
        Assert.Equal("xlsx", file.FileExtension);

        using var stream = new MemoryStream(file.Content);
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheets.First();

        Assert.Equal("Name", worksheet.Cell(1, 1).GetString());
        Assert.Equal("Note", worksheet.Cell(1, 2).GetString());
        Assert.Equal("Row One", worksheet.Cell(2, 1).GetString());
        Assert.Equal("First", worksheet.Cell(2, 2).GetString());
        Assert.Equal("Row Two", worksheet.Cell(3, 1).GetString());
        Assert.Equal("Second", worksheet.Cell(3, 2).GetString());
    }

    [Fact]
    public void Export_xlsx_sanitizes_sheet_names_over_31_chars_and_invalid_chars()
    {
        var exporter = new ReportExporter();
        var longName = "This/Sheet:Name*Is[Way]Too?Long\\ForExcel";

        var file = exporter.Export(Array.Empty<SampleRow>(), Columns, longName, ReportExportFormat.Xlsx);

        using var stream = new MemoryStream(file.Content);
        using var workbook = new XLWorkbook(stream);
        var sheetName = workbook.Worksheets.First().Name;

        Assert.True(sheetName.Length <= 31);
        Assert.DoesNotContain('/', sheetName);
        Assert.DoesNotContain(':', sheetName);
        Assert.DoesNotContain('*', sheetName);
    }
}
