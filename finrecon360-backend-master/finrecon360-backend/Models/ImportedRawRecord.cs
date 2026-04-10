namespace finrecon360_backend.Models
{
    public class ImportedRawRecord
    {
        public Guid ImportedRawRecordId { get; set; }
        public Guid ImportBatchId { get; set; }
        public int? RowNumber { get; set; }
        public string SourcePayloadJson { get; set; } = string.Empty;
        public string NormalizationStatus { get; set; } = "PENDING";
        public string? NormalizationErrors { get; set; }
        public DateTime CreatedAt { get; set; }

        public ImportBatch? ImportBatch { get; set; }
        public ICollection<ImportedNormalizedRecord> NormalizedRecords { get; set; } = new List<ImportedNormalizedRecord>();
    }
}
