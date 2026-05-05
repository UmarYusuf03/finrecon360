namespace finrecon360_backend.Dtos.Reconciliation
{
    // ─── Response DTOs ──────────────────────────────────────────────────────────

    public class ReconciliationMatchGroupResponse
    {
        public Guid ReconciliationMatchGroupId { get; set; }
        public Guid ImportBatchId { get; set; }
        public string MatchLevel { get; set; } = null!;
        public string? SettlementKey { get; set; }
        public bool IsConfirmed { get; set; }
        public Guid? ConfirmedByUserId { get; set; }
        public DateTime? ConfirmedAt { get; set; }
        public bool IsJournalPosted { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public List<ReconciliationMatchedRecordResponse> MatchedRecords { get; set; } = new();
    }

    public class ReconciliationMatchedRecordResponse
    {
        public Guid ReconciliationMatchedRecordId { get; set; }
        public Guid ImportedNormalizedRecordId { get; set; }
        public string SourceType { get; set; } = null!;
        public decimal MatchAmount { get; set; }

        // Denormalized from ImportedNormalizedRecord for quick display
        public DateTime? TransactionDate { get; set; }
        public string? ReferenceNumber { get; set; }
        public decimal? GrossAmount { get; set; }
        public decimal? ProcessingFee { get; set; }
        public decimal NetAmount { get; set; }
        public string Currency { get; set; } = "LKR";
        public string MatchStatus { get; set; } = null!;
    }

    public class ReconciliationEventResponse
    {
        public Guid ReconciliationEventId { get; set; }
        public Guid ImportBatchId { get; set; }
        public Guid ImportedNormalizedRecordId { get; set; }
        public string EventType { get; set; } = null!;
        public string Stage { get; set; } = null!;
        public string SourceType { get; set; } = null!;
        public string Status { get; set; } = null!;
        public string? DetailJson { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ResolvedAt { get; set; }
    }

    public class WaitingRecordResponse
    {
        public Guid ImportedNormalizedRecordId { get; set; }
        public Guid ImportBatchId { get; set; }
        public DateTime TransactionDate { get; set; }
        public string? ReferenceNumber { get; set; }
        public string? Description { get; set; }
        public decimal? GrossAmount { get; set; }
        public decimal? ProcessingFee { get; set; }
        public decimal NetAmount { get; set; }
        public string Currency { get; set; } = "LKR";
        public string MatchStatus { get; set; } = null!;
    }

    public class JournalEntryResponse
    {
        public Guid JournalEntryId { get; set; }
        public Guid? TransactionId { get; set; }
        public Guid? ReconciliationMatchGroupId { get; set; }
        public string EntryType { get; set; } = null!;
        public decimal Amount { get; set; }
        public string Currency { get; set; } = null!;
        public DateTime PostedAt { get; set; }
        public Guid? PostedByUserId { get; set; }
        public string? Notes { get; set; }
    }

    // ─── Request DTOs ───────────────────────────────────────────────────────────

    public class AttachSettlementIdRequest
    {
        public string SettlementId { get; set; } = null!;
    }

    public class PostJournalRequest
    {
        public string? Notes { get; set; }
    }
}
