namespace finrecon360_backend.Models
{
    /// <summary>
    /// Represents an event logged by the reconciliation engine during a matching run.
    /// Events are append-only and are used for debugging, exception handling, and audit.
    ///
    /// EventType values:
    ///   MatchFound        — a bilateral match was established successfully
    ///   MatchNotFound     — no corresponding record exists in the partner source
    ///   Variance          — records exist in both sources but amounts/dates differ beyond tolerance
    ///   RequiresReview    — ambiguous match (multiple candidates); routed to manual confirmation queue
    /// </summary>
    public class ReconciliationEvent
    {
        public Guid ReconciliationEventId { get; set; }

        // Set when the event relates to a confirmed or pending match group.
        public Guid? ReconciliationMatchGroupId { get; set; }

        // The imported record that caused this event (Source A side).
        public Guid? ImportedNormalizedRecordId { get; set; }

        // MatchFound | MatchNotFound | Variance | RequiresReview
        public string EventType { get; set; } = string.Empty;

        // Level1..Level6 — which rule was running when the event occurred.
        public string MatchLevel { get; set; } = string.Empty;

        // Human-readable detail: variance amount, candidate counts, etc.
        public string? Details { get; set; }

        public string? Stage { get; set; }
        public string? SourceType { get; set; }
        public string? Status { get; set; }
        public string? DetailJson { get; set; }
        public Guid? ImportBatchId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ResolvedAt { get; set; }

        // Navigation
        public ReconciliationMatchGroup? MatchGroup { get; set; }
    }
}
