using System.Text;
using ClosedXML.Excel;

namespace finrecon360_backend.Services.Export
{
    public enum ReportExportFormat
    {
        Csv,
        Xlsx
    }

    public record ExportColumn<T>(string Header, Func<T, string?> Value);

    public record ExportFile(byte[] Content, string ContentType, string FileExtension);

    /// <summary>
    /// Shared CSV/XLSX writer for every "Export" button in the admin UI. Controllers build the
    /// rows and column mapping for their own DTOs and hand them here so every export screen gets
    /// the same escaping, formatting, and row-cap behavior instead of reinventing it per feature.
    /// </summary>
    public interface IReportExporter
    {
        /// <summary>Row cap enforced by every export endpoint. Callers should check this against
        /// their result count before calling Export, and return a clear error instead of silently
        /// truncating.</summary>
        int MaxRows { get; }

        bool TryParseFormat(string? format, out ReportExportFormat parsed);

        ExportFile Export<T>(
            IReadOnlyCollection<T> rows,
            IReadOnlyList<ExportColumn<T>> columns,
            string sheetName,
            ReportExportFormat format);
    }

    public class ReportExporter : IReportExporter
    {
        public int MaxRows => 10_000;

        public bool TryParseFormat(string? format, out ReportExportFormat parsed)
        {
            switch ((format ?? "csv").Trim().ToLowerInvariant())
            {
                case "csv":
                    parsed = ReportExportFormat.Csv;
                    return true;
                case "xlsx":
                    parsed = ReportExportFormat.Xlsx;
                    return true;
                default:
                    parsed = ReportExportFormat.Csv;
                    return false;
            }
        }

        public ExportFile Export<T>(
            IReadOnlyCollection<T> rows,
            IReadOnlyList<ExportColumn<T>> columns,
            string sheetName,
            ReportExportFormat format)
        {
            return format switch
            {
                ReportExportFormat.Xlsx => new ExportFile(
                    ToXlsx(rows, columns, sheetName),
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    "xlsx"),
                _ => new ExportFile(
                    ToCsv(rows, columns),
                    "text/csv",
                    "csv"),
            };
        }

        private static byte[] ToCsv<T>(IEnumerable<T> rows, IReadOnlyList<ExportColumn<T>> columns)
        {
            var builder = new StringBuilder();
            builder.Append(string.Join(",", columns.Select(c => EscapeCsvField(c.Header))));
            builder.Append("\r\n");

            foreach (var row in rows)
            {
                builder.Append(string.Join(",", columns.Select(c => EscapeCsvField(c.Value(row) ?? string.Empty))));
                builder.Append("\r\n");
            }

            // UTF-8 BOM so Excel opens the file with non-ASCII content rendered correctly.
            var preamble = Encoding.UTF8.GetPreamble();
            var body = Encoding.UTF8.GetBytes(builder.ToString());
            var result = new byte[preamble.Length + body.Length];
            preamble.CopyTo(result, 0);
            body.CopyTo(result, preamble.Length);
            return result;
        }

        private static string EscapeCsvField(string field)
        {
            var needsQuoting = field.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0;
            if (!needsQuoting)
            {
                return field;
            }

            return $"\"{field.Replace("\"", "\"\"")}\"";
        }

        private static byte[] ToXlsx<T>(IEnumerable<T> rows, IReadOnlyList<ExportColumn<T>> columns, string sheetName)
        {
            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add(SanitizeSheetName(sheetName));

            for (var i = 0; i < columns.Count; i++)
            {
                var cell = worksheet.Cell(1, i + 1);
                cell.Value = columns[i].Header;
                cell.Style.Font.Bold = true;
            }

            var rowIndex = 2;
            foreach (var row in rows)
            {
                for (var i = 0; i < columns.Count; i++)
                {
                    worksheet.Cell(rowIndex, i + 1).Value = columns[i].Value(row) ?? string.Empty;
                }

                rowIndex++;
            }

            worksheet.SheetView.FreezeRows(1);
            worksheet.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        private static string SanitizeSheetName(string name)
        {
            var invalidChars = new[] { '\\', '/', '?', '*', '[', ']', ':' };
            var sanitized = new string(name.Where(c => !invalidChars.Contains(c)).ToArray());
            if (string.IsNullOrWhiteSpace(sanitized))
            {
                sanitized = "Report";
            }

            return sanitized.Length > 31 ? sanitized[..31] : sanitized;
        }
    }
}
