using System.Globalization;
using finrecon360_backend.Models;

namespace finrecon360_backend.Services
{
    public record NormalizationResult(ImportedNormalizedRecord Normalized, IReadOnlyList<string> Errors);

    public interface IImportNormalizationService
    {
        IReadOnlyList<string> ValidateRow(Dictionary<string, string?> row, Dictionary<string, string> mappings);
        NormalizationResult Normalize(Guid batchId, Guid rawRecordId, Dictionary<string, string?> row, Dictionary<string, string> mappings);
    }

    public class ImportNormalizationService : IImportNormalizationService
    {
        private static readonly string[] DateFormats =
        {
            "dd/MM/yyyy",
            "d/M/yyyy",
            "MM/dd/yyyy",
            "M/d/yyyy",
            "yyyy-MM-dd",
            "dd-MM-yyyy",
            "d-M-yyyy",
            "yyyy/MM/dd",
            "dd.MM.yyyy",
            "d.M.yyyy"
        };

        public IReadOnlyList<string> ValidateRow(Dictionary<string, string?> row, Dictionary<string, string> mappings)
        {
            var errors = new List<string>();

            var transactionDate = ReadDate(row, mappings, "TransactionDate", errors, required: true);
            var referenceNumber = ReadString(row, mappings, "ReferenceNumber");
            if (string.IsNullOrWhiteSpace(referenceNumber))
            {
                errors.Add("Reference number is required.");
            }

            var (debit, debitError) = ReadDecimal(row, mappings, "DebitAmount");
            if (!string.IsNullOrWhiteSpace(debitError))
            {
                errors.Add(debitError);
            }

            var (credit, creditError) = ReadDecimal(row, mappings, "CreditAmount");
            if (!string.IsNullOrWhiteSpace(creditError))
            {
                errors.Add(creditError);
            }

            var (net, netError) = ReadDecimal(row, mappings, "NetAmount");
            if (!string.IsNullOrWhiteSpace(netError))
            {
                errors.Add(netError);
            }

            if (!debit.HasValue && !credit.HasValue && !net.HasValue)
            {
                errors.Add("At least one amount (debit, credit, or net) is required.");
            }

            if (debit.HasValue && credit.HasValue && debit.Value != 0m && credit.Value != 0m)
            {
                errors.Add("Both debit and credit amounts are populated. Only one should be provided.");
            }

            if (net.HasValue && debit.HasValue && credit.HasValue)
            {
                var expectedNet = debit.Value - credit.Value;
                if (Math.Abs(expectedNet - net.Value) > 0.01m)
                {
                    errors.Add("Net amount does not match debit/credit values.");
                }
            }

            var currency = ReadString(row, mappings, "Currency");
            if (string.IsNullOrWhiteSpace(currency))
            {
                errors.Add("Currency is required.");
            }

            if (transactionDate == null)
            {
                errors.Add("Transaction date is required.");
            }

            return errors;
        }

        public NormalizationResult Normalize(
            Guid batchId,
            Guid rawRecordId,
            Dictionary<string, string?> row,
            Dictionary<string, string> mappings)
        {
            var errors = ValidateRow(row, mappings).ToList();

            var debit = ReadDecimal(row, mappings, "DebitAmount").Value ?? 0m;
            var credit = ReadDecimal(row, mappings, "CreditAmount").Value ?? 0m;
            var netAmount = ReadDecimal(row, mappings, "NetAmount").Value;

            if (!netAmount.HasValue)
            {
                netAmount = debit - credit;
            }

            if (netAmount.HasValue && debit == 0m && credit == 0m)
            {
                if (netAmount.Value >= 0m)
                {
                    debit = netAmount.Value;
                }
                else
                {
                    credit = Math.Abs(netAmount.Value);
                }
            }

            var currency = NormalizeCurrency(ReadString(row, mappings, "Currency")) ?? "LKR";

            var normalized = new ImportedNormalizedRecord
            {
                ImportedNormalizedRecordId = Guid.NewGuid(),
                ImportBatchId = batchId,
                SourceRawRecordId = rawRecordId,
                TransactionDate = ReadDate(row, mappings, "TransactionDate", errors, required: true) ?? DateTime.UtcNow,
                PostingDate = ReadDate(row, mappings, "PostingDate", errors, required: false),
                ReferenceNumber = NormalizeText(ReadString(row, mappings, "ReferenceNumber")),
                Description = NormalizeText(ReadString(row, mappings, "Description")),
                AccountCode = NormalizeAccountCode(ReadString(row, mappings, "AccountCode")),
                AccountName = NormalizeText(ReadString(row, mappings, "AccountName")),
                DebitAmount = debit,
                CreditAmount = credit,
                NetAmount = netAmount ?? 0m,
                Currency = currency,
                CreatedAt = DateTime.UtcNow
            };

            return new NormalizationResult(normalized, errors);
        }

        private static string? NormalizeText(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static string? NormalizeAccountCode(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return value.Trim().Replace(" ", string.Empty).ToUpperInvariant();
        }

        private static string? NormalizeCurrency(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var trimmed = value.Trim().ToUpperInvariant();
            if (trimmed.Length > 3)
            {
                trimmed = trimmed.Substring(0, 3);
            }

            return trimmed;
        }

        private static string? ReadString(Dictionary<string, string?> row, Dictionary<string, string> mappings, string canonicalField)
        {
            if (!mappings.TryGetValue(canonicalField, out var sourceColumn) || string.IsNullOrWhiteSpace(sourceColumn))
            {
                return null;
            }

            row.TryGetValue(sourceColumn, out var value);
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static (decimal? Value, string? Error) ReadDecimal(Dictionary<string, string?> row, Dictionary<string, string> mappings, string canonicalField)
        {
            var raw = ReadString(row, mappings, canonicalField);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return (null, null);
            }

            if (decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var invariant))
            {
                return (invariant, null);
            }

            if (decimal.TryParse(raw, NumberStyles.Any, CultureInfo.CurrentCulture, out var current))
            {
                return (current, null);
            }

            return (null, $"{canonicalField} is not a valid number.");
        }

        private static DateTime? ReadDate(
            Dictionary<string, string?> row,
            Dictionary<string, string> mappings,
            string canonicalField,
            List<string> errors,
            bool required)
        {
            var raw = ReadString(row, mappings, canonicalField);
            if (string.IsNullOrWhiteSpace(raw))
            {
                if (required)
                {
                    return null;
                }

                return null;
            }

            if (DateTime.TryParseExact(raw, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var exact))
            {
                return exact;
            }

            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var invariant))
            {
                return invariant;
            }

            if (DateTime.TryParse(raw, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out var current))
            {
                return current;
            }

            errors.Add($"{canonicalField} has invalid date value '{raw}'.");
            return null;
        }
    }
}
