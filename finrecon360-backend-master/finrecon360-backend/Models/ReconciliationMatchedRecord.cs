namespace finrecon360_backend.Models
{
    /// <summary>
    /// Links an individual ImportedNormalizedRecord to a ReconciliationMatchGroup.
    /// A group will typically have exactly two matched records: one from Source A and one from Source B.
    /// For Settlement Match (Level6), a group may link many GATEWAY records to one BANK record.
    /// </summary>
    public class ReconciliationMatchedRecord
    {
        public Guid ReconciliationMatchedRecordId { get; set; }

        // The group this record belongs to.
        public Guid ReconciliationMatchGroupId { get; set; }

        // The normalised record from the import pipeline.
        public Guid ImportedNormalizedRecordId { get; set; }

        // The source system this record came from (GATEWAY | BANK | POS | ERP | STAFF).
        // Denormalised here so that query-side code can identify roles without a join.
        public string SourceType { get; set; } = string.Empty;

        public DateTime LinkedAt { get; set; } = DateTime.UtcNow;

        // Navigation
        public ReconciliationMatchGroup? MatchGroup { get; set; }
        public ImportedNormalizedRecord? ImportedRecord { get; set; }
    }
}
