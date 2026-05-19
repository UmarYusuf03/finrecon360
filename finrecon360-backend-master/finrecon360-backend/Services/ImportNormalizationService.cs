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
            _ = ReadString(row, mappings, "ReferenceNumber");

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

            var (gross, grossError) = ReadDecimal(row, mappings, "GrossAmount");
            if (!string.IsNullOrWhiteSpace(grossError))
            {
                errors.Add(grossError);
            }

            var (fee, feeError) = ReadDecimal(row, mappings, "ProcessingFee");
            if (!string.IsNullOrWhiteSpace(feeError))
            {
                errors.Add(feeError);
            }

            if (!net.HasValue)
            {
                errors.Add("Net amount is required.");
            }

            if (net.HasValue && debit.HasValue && credit.HasValue)
            {
                var expectedNet = debit.Value - credit.Value;
                if (Math.Abs(expectedNet - net.Value) > 0.01m)
                {
                    errors.Add("Net amount does not match debit/credit values.");
                }
            }

            _ = ReadString(row, mappings, "Currency");
            _ = ReadString(row, mappings, "TransactionType");

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
            var grossAmount = ReadDecimal(row, mappings, "GrossAmount").Value;
            var processingFee = ReadDecimal(row, mappings, "ProcessingFee").Value;
            var transactionType = NormalizeTransactionType(ReadString(row, mappings, "TransactionType"));

            if (!netAmount.HasValue)
            {
                netAmount = debit - credit;
            }

            if (netAmount.HasValue && debit == 0m && credit == 0m)
            {
                var netAbs = Math.Abs(netAmount.Value);
                if (transactionType == "DEBIT")
                {
                    debit = netAbs;
                    credit = 0m;
                }
                else if (transactionType == "CREDIT")
                {
                    credit = netAbs;
                    debit = 0m;
                }
                else if (netAmount.Value >= 0m)
                {
                    debit = netAbs;
                }
                else
                {
                    credit = netAbs;
                }
            }

            var currency = NormalizeCurrency(ReadString(row, mappings, "Currency")) ?? "LKR";

            var normalized = new ImportedNormalizedRecord
            {
                ImportedNormalizedRecordId = Guid.NewGuid(),
                ImportBatchId = batchId,
                SourceRawRecordId = rawRecordId,
                TransactionDate = ReadDate(row, mappings, "TransactionDate", errors, required: true) ?? DateTime.UtcNow,
                TransactionType = transactionType,
                PostingDate = ReadDate(row, mappings, "PostingDate", errors, required: false),
                ReferenceNumber = NormalizeText(ReadString(row, mappings, "ReferenceNumber")),
                Description = NormalizeText(ReadString(row, mappings, "Description")),
                AccountCode = NormalizeAccountCode(ReadString(row, mappings, "AccountCode")),
                AccountName = NormalizeText(ReadString(row, mappings, "AccountName")),
                GrossAmount = grossAmount,
                ProcessingFee = processingFee,
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

        private static string? NormalizeTransactionType(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var normalized = value.Trim().ToLowerInvariant();
            return normalized switch
            {
                "dr" => "DEBIT",
                "debit" => "DEBIT",
                "cr" => "CREDIT",
                "credit" => "CREDIT",
                _ => null
            };
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
