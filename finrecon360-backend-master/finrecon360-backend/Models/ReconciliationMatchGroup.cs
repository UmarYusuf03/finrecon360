namespace finrecon360_backend.Models
{
    /// <summary>
    /// Represents a group of reconciliation-matched records across different sources.
    /// Created when matching succeeds at any Ironclad level (3 or 4).
    /// Used to track which canonical records have been successfully matched together.
    /// </summary>
    public class ReconciliationMatchGroup
    {
        public Guid ReconciliationMatchGroupId { get; set; }
        public Guid ImportBatchId { get; set; }
        
        /// <summary>
        /// Ironclad stage applied: 'Level3' or 'Level4'.
        /// </summary>
        public string MatchLevel { get; set; } = null!;

        /// <summary>
        /// Canonical settlement key used for matching (AccountCode fallback ReferenceNumber).
        /// </summary>
        public string? SettlementKey { get; set; }

        /// <summary>
        /// Primary reconciliation event that triggered this match group creation.
        /// References ReconciliationEvent.ReconciliationEventId.
        /// </summary>
        public Guid? PrimaryEventId { get; set; }

        /// <summary>
        /// Event-specific match metadata (e.g., gross mismatch tolerance %, settlement net variance).
        /// Stored as JSON for flexibility.
        /// </summary>
        public string? MatchMetadataJson { get; set; }

        /// <summary>
        /// True once a tenant accountant has confirmed this match group.
        /// Journal posting is gated until IsConfirmed = true.
        /// </summary>
        public bool IsConfirmed { get; set; } = false;

        public Guid? ConfirmedByUserId { get; set; }
        public DateTime? ConfirmedAt { get; set; }

        /// <summary>
        /// True once the journal entry has been posted for this match group.
        /// </summary>
        public bool IsJournalPosted { get; set; } = false;

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public ImportBatch? ImportBatch { get; set; }
        public ICollection<ReconciliationMatchedRecord> MatchedRecords { get; set; } = new List<ReconciliationMatchedRecord>();
    }
}
