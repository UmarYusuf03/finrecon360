using System.Text;

namespace ScenarioSeeder;

/// <summary>
/// One row of an import CSV, fields named after finrecon360-backend's canonical import schema
/// (Controllers/ImportsController.cs / ImportNormalizationService). The CSV header row this tool
/// writes is exactly <see cref="Headers"/>, so the mapping step sent to /api/imports/{id}/mapping
/// can be a trivial identity mapping (canonical name -> canonical name) instead of guessing at a
/// human-named header like "Transaction Date".
/// </summary>
public sealed class ImportRow
{
    public string? TransactionDate;
    public string? PostingDate;
    public string? ReferenceNumber;
    public string? SettlementId;
    public string? Description;
    public string? AccountCode;
    public string? AccountName;
    public string? BatchNumber;
    public string? TerminalId;
    public string? MerchantId;
    public string? GrossAmount;
    public string? ProcessingFee;
    public string? DebitAmount;
    public string? CreditAmount;
    public string? NetAmount;
    public string? Currency;

    public static readonly string[] Headers =
    {
        "TransactionDate", "PostingDate", "ReferenceNumber", "SettlementId", "Description",
        "AccountCode", "AccountName", "BatchNumber", "TerminalId", "MerchantId",
        "GrossAmount", "ProcessingFee", "DebitAmount", "CreditAmount", "NetAmount", "Currency"
    };

    public string?[] ToFields() => new[]
    {
        TransactionDate, PostingDate, ReferenceNumber, SettlementId, Description,
        AccountCode, AccountName, BatchNumber, TerminalId, MerchantId,
        GrossAmount, ProcessingFee, DebitAmount, CreditAmount, NetAmount, Currency
    };
}

public static class CsvWriterUtil
{
    public static string Build(IEnumerable<ImportRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", ImportRow.Headers));
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",", row.ToFields().Select(Escape)));
        }

        return sb.ToString();
    }

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        return value;
    }
}
