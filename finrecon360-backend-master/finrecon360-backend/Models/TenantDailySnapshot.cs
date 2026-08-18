namespace finrecon360_backend.Models
{
    /// <summary>
    /// One row per SnapshotDate — the tenant-wide counterpart to ReconciliationDailySnapshot
    /// (which is per MatchLevel). Together they cover the full "typical outputs" list from
    /// Section 17 of the system architecture doc: reconciliation status summaries and unmatched
    /// item counts (ReconciliationDailySnapshot), approval backlog, journal posting summaries,
    /// and bank account reconciliation progress (this table). Kept as a separate table rather
    /// than broadened into ReconciliationDailySnapshot's per-level shape because none of these
    /// columns are naturally per-MatchLevel — forcing them into that shape would mean repeating
    /// the same tenant-wide number on every level row.
    /// </summary>
    public class TenantDailySnapshot
    {
        public Guid TenantDailySnapshotId { get; set; }

        public DateTime SnapshotDate { get; set; }

        // Approval backlog: Transactions still Pending as of when this snapshot ran, restricted
        // to ones created on this day (count), plus the age of the single oldest Pending
        // transaction tenant-wide at that moment (not day-scoped — "how stale is the backlog
        // right now" is the useful operational number, not "how stale were today's specifically").
        public int PendingApprovalCount { get; set; }
        public decimal? OldestPendingApprovalAgeHours { get; set; }

        // Journal posting summary: entries posted on this day, and the sum of their positive
        // (debit-side) amounts — by double-entry construction this equals the credit-side sum,
        // so it's a clean single number for "total ledger throughput that day".
        public int JournalEntriesPostedCount { get; set; }
        public decimal JournalDebitAmountPosted { get; set; }

        // Bank account reconciliation progress: of the committed BANK-source records dated this
        // day, how many have been matched (MatchStatus != PENDING) as of when this snapshot ran.
        public int BankRecordsTotalCount { get; set; }
        public int BankRecordsMatchedCount { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
