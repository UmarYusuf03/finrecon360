namespace finrecon360_backend.Models
{
    public class ImportedNormalizedRecord
    {
        public Guid ImportedNormalizedRecordId { get; set; }
        public Guid ImportBatchId { get; set; }
        public Guid? SourceRawRecordId { get; set; }

        public DateTime TransactionDate { get; set; }
        public string? TransactionType { get; set; }
        public DateTime? PostingDate { get; set; }
        public string? ReferenceNumber { get; set; }
        public string? Description { get; set; }
        public string? AccountCode { get; set; }
        public string? AccountName { get; set; }
        public decimal? GrossAmount { get; set; }
        public decimal? ProcessingFee { get; set; }
        public decimal DebitAmount { get; set; }
        public decimal CreditAmount { get; set; }
        public decimal NetAmount { get; set; }
        public string Currency { get; set; } = "LKR";
        public DateTime CreatedAt { get; set; }
        
        public string? MatchStatus { get; set; } = "PENDING";
        public string? SettlementId { get; set; }
        public string? SettlementKey { get; set; }

        // Identifiers extracted from a bank narrative/description by PosIdentifierExtractor at
        // commit time (per ImportMappingTemplate.ExtractionPatternsJson), normalized (trimmed,
        // uppercased, leading zeros stripped from BatchNumber) so they're clean grouping keys
        // for PosSettlementMatchWorker (Level7) rather than raw substrings.
        public string? BatchNumber { get; set; }
        public string? TerminalId { get; set; }
        public string? MerchantId { get; set; }

        public ImportBatch? ImportBatch { get; set; }
        public ImportedRawRecord? SourceRawRecord { get; set; }
    }
}
