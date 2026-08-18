namespace finrecon360_backend.Models
{
    /// <summary>
    /// One precomputed rollup row per (SnapshotDate, MatchLevel), populated once daily by
    /// ReconciliationSnapshotHostedService for the previous UTC day. ReconciliationMatchGroup and
    /// ReconciliationEvent are current-state/append-only tables with no cheap way to ask "what did
    /// match-rate look like on a given day in the past" — this table is that history, computed
    /// once so trend report reads never have to scan the transactional tables (Section 17's
    /// stated goal for the reporting layer).
    /// </summary>
    public class ReconciliationDailySnapshot
    {
        public Guid ReconciliationDailySnapshotId { get; set; }

        // The UTC calendar day this row summarizes (date-only; time component ignored).
        public DateTime SnapshotDate { get; set; }

        // Level1..Level4, Level6, Level7 — matches ReconciliationMatchGroup.MatchLevel /
        // ReconciliationEvent.MatchLevel. Level5 is retired (see ReconciliationCycleHostedService)
        // and will not appear in new rows.
        public string MatchLevel { get; set; } = string.Empty;

        // Match groups created (first produced by a matching worker) on this day for this level.
        public int MatchedCount { get; set; }

        // Of those (or older groups still pending), how many were confirmed on this day.
        public int ConfirmedCount { get; set; }

        // Variance events logged on this day for this level.
        public int ExceptionCount { get; set; }

        // Currently-pending BANK records whose original TransactionDate falls on this day.
        // Only populated for Level4 today — unmatched detection is scoped to the Expense Match
        // (BANK vs approved card cashout) rule, mirroring ReconciliationMatchConfirmationService.
        public int UnmatchedCount { get; set; }

        // Average (ConfirmedAt - CreatedAt) in hours for groups confirmed on this day, for this
        // level. Null when nothing was confirmed that day (not the same as zero — zero would mean
        // "confirmed instantly", null means "no confirmations to measure").
        public decimal? AverageTimeToMatchHours { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
