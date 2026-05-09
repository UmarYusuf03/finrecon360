namespace finrecon360_backend.Models
{
    /// <summary>
    /// Join entity linking a normalized import record to a reconciliation match group.
    /// Tracks which source ('ERP', 'Gateway', 'Bank') each matched record came from.
    /// </summary>
    public class ReconciliationMatchedRecord
    {
        public Guid ReconciliationMatchedRecordId { get; set; }
        public Guid ReconciliationMatchGroupId { get; set; }
        public Guid ImportedNormalizedRecordId { get; set; }

        /// <summary>
        /// Source type of the matched record: 'ERP', 'Gateway', 'Bank', or 'POS'.
        /// </summary>
        public string SourceType { get; set; } = null!;

        /// <summary>
        /// Reference amount from this record used in the match (e.g., GrossAmount, NetAmount).
        /// </summary>
        public decimal MatchAmount { get; set; }

        public DateTime CreatedAt { get; set; }

        public ReconciliationMatchGroup? ReconciliationMatchGroup { get; set; }
        public ImportedNormalizedRecord? ImportedNormalizedRecord { get; set; }
    }
}
