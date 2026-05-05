namespace finrecon360_backend.Models
{
    /// <summary>
    /// Represents a reconciliation event in the Ironclad workflow.
    /// Events are triggered during import commit and record the outcome of matching attempts.
    /// Supports the 6-event rule matrix: MatchFound, MatchNotFound, PartialMatch, Variance, ProcessingFeeAdjustment, ManualReview.
    /// </summary>
    public class ReconciliationEvent
    {
        public Guid ReconciliationEventId { get; set; }
        public Guid ImportBatchId { get; set; }
        public Guid ImportedNormalizedRecordId { get; set; }

        /// <summary>
        /// Event type from Ironclad 6-event matrix:
        /// MatchFound, MatchNotFound, PartialMatch, Variance, ProcessingFeeAdjustment, ManualReview.
        /// </summary>
        public string EventType { get; set; } = null!;

        /// <summary>
        /// Ironclad stage where this event occurred: 'Level3' or 'Level4'.
        /// </summary>
        public string Stage { get; set; } = null!;

        /// <summary>
        /// Source type of the record that triggered this event: 'ERP', 'Gateway', 'Bank', or 'POS'.
        /// </summary>
        public string SourceType { get; set; } = null!;

        /// <summary>
        /// Event status: 'Pending', 'Completed', 'RequiresReview', 'Resolved'.
        /// </summary>
        public string Status { get; set; } = "Pending";

        /// <summary>
        /// Event-specific details (e.g., matched record IDs, variance amount, manual review reason).
        /// Stored as JSON for flexibility.
        /// </summary>
        public string? DetailJson { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? ResolvedAt { get; set; }

        public ImportBatch? ImportBatch { get; set; }
        public ImportedNormalizedRecord? ImportedNormalizedRecord { get; set; }
    }
}
