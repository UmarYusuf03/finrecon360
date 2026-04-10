namespace finrecon360_backend.Models
{
    public class ImportedNormalizedRecord
    {
        public Guid ImportedNormalizedRecordId { get; set; }
        public Guid ImportBatchId { get; set; }
        public Guid? SourceRawRecordId { get; set; }

        public DateTime TransactionDate { get; set; }
        public DateTime? PostingDate { get; set; }
        public string? ReferenceNumber { get; set; }
        public string? Description { get; set; }
        public string? AccountCode { get; set; }
        public string? AccountName { get; set; }
        public decimal DebitAmount { get; set; }
        public decimal CreditAmount { get; set; }
        public decimal NetAmount { get; set; }
        public string Currency { get; set; } = "LKR";
        public DateTime CreatedAt { get; set; }

        public ImportBatch? ImportBatch { get; set; }
        public ImportedRawRecord? SourceRawRecord { get; set; }
    }
}
