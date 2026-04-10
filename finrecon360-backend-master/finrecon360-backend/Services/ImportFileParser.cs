using System.Globalization;
using System.IO.Compression;
using ClosedXML.Excel;
using Microsoft.VisualBasic.FileIO;

namespace finrecon360_backend.Services
{
    public record ParsedImportFile(
        IReadOnlyList<string> Headers,
        IReadOnlyList<Dictionary<string, string?>> Rows,
        int TotalRows);

    public interface IImportFileParser
    {
        Task<ParsedImportFile> ParseAsync(string filePath, CancellationToken cancellationToken = default);
    }

    public class ImportFileParser : IImportFileParser
    {
        private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".csv", ".xlsx"
        };

        public Task<ParsedImportFile> ParseAsync(string filePath, CancellationToken cancellationToken = default)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("Import file not found.", filePath);
            }

            var extension = Path.GetExtension(filePath);
            if (!SupportedExtensions.Contains(extension))
            {
                throw new InvalidOperationException("Unsupported file format. Only CSV and XLSX are supported.");
            }

            return extension.Equals(".csv", StringComparison.OrdinalIgnoreCase)
                ? Task.FromResult(ParseCsv(filePath))
                : Task.FromResult(ParseXlsx(filePath));
        }

        private static ParsedImportFile ParseCsv(string filePath)
        {
            using var parser = new TextFieldParser(filePath)
            {
                TextFieldType = FieldType.Delimited,
                HasFieldsEnclosedInQuotes = true,
                TrimWhiteSpace = false
            };
            parser.SetDelimiters(",");

            if (parser.EndOfData)
            {
                return new ParsedImportFile(Array.Empty<string>(), Array.Empty<Dictionary<string, string?>>(), 0);
            }

            var headerFields = parser.ReadFields() ?? Array.Empty<string>();
            var headers = NormalizeHeaders(headerFields);
            var rows = new List<Dictionary<string, string?>>();

            while (!parser.EndOfData)
            {
                var values = parser.ReadFields() ?? Array.Empty<string>();
                var row = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < headers.Count; i++)
                {
                    var value = i < values.Length ? values[i] : null;
                    row[headers[i]] = string.IsNullOrWhiteSpace(value) ? null : value?.Trim();
                }
                rows.Add(row);
            }

            return new ParsedImportFile(headers, rows, rows.Count);
        }

        private static ParsedImportFile ParseXlsx(string filePath)
        {
            using var workbook = new XLWorkbook(filePath);
            var sheet = workbook.Worksheets.FirstOrDefault()
                ?? throw new InvalidOperationException("The workbook does not contain any worksheet.");

            var usedRange = sheet.RangeUsed();
            if (usedRange == null)
            {
                return new ParsedImportFile(Array.Empty<string>(), Array.Empty<Dictionary<string, string?>>(), 0);
            }

            var firstRow = usedRange.FirstRowUsed();
            var firstColumn = usedRange.RangeAddress.FirstAddress.ColumnNumber;
            var lastColumn = usedRange.RangeAddress.LastAddress.ColumnNumber;
            var headers = new List<string>();
            for (var col = firstColumn; col <= lastColumn; col++)
            {
                var raw = firstRow.Cell(col).GetFormattedString();
                headers.Add(NormalizeHeader(raw, headers.Count));
            }

            var rows = new List<Dictionary<string, string?>>();
            var lastRow = usedRange.RangeAddress.LastAddress.RowNumber;
            for (var rowIndex = firstRow.RowNumber() + 1; rowIndex <= lastRow; rowIndex++)
            {
                var row = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                var hasValue = false;

                for (var col = firstColumn; col <= lastColumn; col++)
                {
                    var header = headers[col - firstColumn];
                    var value = sheet.Cell(rowIndex, col).GetFormattedString();
                    var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
                    if (!string.IsNullOrWhiteSpace(normalized))
                    {
                        hasValue = true;
                    }
                    row[header] = normalized;
                }

                if (hasValue)
                {
                    rows.Add(row);
                }
            }

            return new ParsedImportFile(headers, rows, rows.Count);
        }

        private static List<string> NormalizeHeaders(IReadOnlyList<string> rawHeaders)
        {
            var result = new List<string>(rawHeaders.Count);
            for (var i = 0; i < rawHeaders.Count; i++)
            {
                result.Add(NormalizeHeader(rawHeaders[i], i));
            }
            return result;
        }

        private static string NormalizeHeader(string? value, int index)
        {
            var header = string.IsNullOrWhiteSpace(value) ? $"Column{index + 1}" : value.Trim();
            return header;
        }
    }
}
