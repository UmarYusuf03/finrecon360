namespace finrecon360_backend.Dtos.Dashboard
{
    // ─── Response DTOs ──────────────────────────────────────────────────────────

    public class ReportSnapshotDto
    {
        public Guid ReportSnapshotId { get; set; }
        public DateTime SnapshotDate { get; set; }
        public int TotalUnmatchedCardCashouts { get; set; }
        public int PendingExceptions { get; set; }
        public int TotalJournalReady { get; set; }
        public decimal ReconciliationCompletionPercentage { get; set; }
        public int TotalMatchGroupsConfirmed { get; set; }
        public decimal TotalFeeAdjustments { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
