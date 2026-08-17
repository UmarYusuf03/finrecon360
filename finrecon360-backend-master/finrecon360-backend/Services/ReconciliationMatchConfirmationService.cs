using finrecon360_backend.Data;
using finrecon360_backend.Models;
using Microsoft.EntityFrameworkCore;
using ReconCanon = finrecon360_backend.Services.Reconciliation;

namespace finrecon360_backend.Services
{
    // ── DTOs ─────────────────────────────────────────────────────────────────────────

    public record PendingMatchSummary(
        Guid MatchGroupId,
        string MatchLevel,
        string SettlementKey,
        decimal MatchedAmount,
        decimal Variance,
        string Status,
        DateTime CreatedAt,
        List<MatchedRecordDetail> Records);

    public record MatchedRecordDetail(
        Guid ImportedNormalizedRecordId,
        string SourceType,
        DateTime TransactionDate,
        decimal NetAmount,
        string? ReferenceNumber,
        string? AccountCode,
        string? Description);

    /// <summary>
    /// Unmatched queue item — a BANK (or other Source B) record that has no reconciliation
    /// match group yet. The Hint explains WHY the match is blocked.
    /// </summary>
    public record UnmatchedQueueItem(
        Guid ImportedNormalizedRecordId,
        string SourceType,
        string MatchRule,
        decimal Amount,
        DateTime TransactionDate,
        string? ReferenceNumber,
        string UnmatchedReason,
        UnmatchedHint? Hint);

    public record UnmatchedHint(
        string HintType,           // NoPossibleMatch | MatchingItemPendingApproval | MatchingItemNeedsBankMatch | AmountVariance | SettlementIdMissing
        Guid? RelatedTransactionId,
        string? RelatedDescription,
        decimal? RelatedAmount,
        string HintMessage);

    public record MatcherSummary(
        int TotalPendingConfirmations,
        int TotalExceptions,
        int TotalUnmatched,
        List<RuleSummary> Rules);

    public record RuleSummary(
        string MatchLevel,
        int PendingConfirmations,
        int Exceptions,
        int Unmatched);


    // ── Interface ─────────────────────────────────────────────────────────────────────

    public interface IReconciliationMatchConfirmationService
    {
        Task<List<PendingMatchSummary>> GetPendingMatchesAsync(
            TenantDbContext db, string? matchLevel, CancellationToken ct);

        Task<bool> ConfirmMatchAsync(
            TenantDbContext db, Guid matchGroupId, string? changeNote,
            Guid confirmedByUserId, CancellationToken ct);

        Task<bool> RejectMatchAsync(
            TenantDbContext db, Guid matchGroupId, string rejectionReason,
            Guid rejectedByUserId, CancellationToken ct);

        Task<List<UnmatchedQueueItem>> GetUnmatchedQueueAsync(
            TenantDbContext db, string? matchRule, DateTime? from, DateTime? to, CancellationToken ct);

        Task<MatcherSummary> GetSummaryAsync(TenantDbContext db, CancellationToken ct);
    }

    // ── Implementation ────────────────────────────────────────────────────────────────

    public class ReconciliationMatchConfirmationService : IReconciliationMatchConfirmationService
    {
        /// <summary>
        /// Returns match groups that are still awaiting human confirmation (IsConfirmed = false).
        /// Optionally filtered to a specific MatchLevel (e.g. "Level4").
        /// </summary>
        public async Task<List<PendingMatchSummary>> GetPendingMatchesAsync(
            TenantDbContext db, string? matchLevel, CancellationToken ct)
        {
            var query = db.ReconciliationMatchGroups
                .AsNoTracking()
                .Include(g => g.MatchedRecords)
                .Where(g => !g.IsConfirmed);

            if (!string.IsNullOrWhiteSpace(matchLevel))
            {
                query = query.Where(g => g.MatchLevel == matchLevel);
            }

            var groups = await query
                .OrderBy(g => g.CreatedAt)
                .ToListAsync(ct);

            var result = new List<PendingMatchSummary>(groups.Count);

            foreach (var group in groups)
            {
                // Load the full imported records for the detail view.
                var recordIds = group.MatchedRecords
                    .Select(mr => mr.ImportedNormalizedRecordId)
                    .ToList();

                var importedRecords = await db.ImportedNormalizedRecords
                    .AsNoTracking()
                    .Where(r => recordIds.Contains(r.ImportedNormalizedRecordId))
                    .ToListAsync(ct);

                // Map each matched record with its source type from the join table.
                var recordDetails = group.MatchedRecords.Select(mr =>
                {
                    var imported = importedRecords.FirstOrDefault(r =>
                        r.ImportedNormalizedRecordId == mr.ImportedNormalizedRecordId);
                    return new MatchedRecordDetail(
                        mr.ImportedNormalizedRecordId,
                        mr.SourceType,
                        imported?.TransactionDate ?? DateTime.MinValue,
                        imported?.NetAmount ?? 0m,
                        imported?.ReferenceNumber,
                        imported?.AccountCode,
                        imported?.Description);
                }).ToList();

                result.Add(new PendingMatchSummary(
                    group.ReconciliationMatchGroupId,
                    group.MatchLevel,
                    group.SettlementKey,
                    group.MatchedAmount,
                    group.Variance,
                    group.Status,
                    group.CreatedAt,
                    recordDetails));
            }

            return result;
        }

        /// <summary>
        /// Confirms a pending match group. Finds the linked transaction (if any in NeedsBankMatch)
        /// and promotes it to JournalReady.
        /// </summary>
        public async Task<bool> ConfirmMatchAsync(
            TenantDbContext db, Guid matchGroupId, string? changeNote,
            Guid confirmedByUserId, CancellationToken ct)
        {
            var group = await db.ReconciliationMatchGroups
                .Include(g => g.MatchedRecords)
                .FirstOrDefaultAsync(g => g.ReconciliationMatchGroupId == matchGroupId, ct);

            if (group == null || group.IsConfirmed)
            {
                return false;
            }

            var now = DateTime.UtcNow;

            group.IsConfirmed = true;
            group.ConfirmedByUserId = confirmedByUserId;
            group.ConfirmedAt = now;
            group.Status = "Confirmed";

            if (group.MatchLevel == "Level7")
            {
                // POS-terminal batch settlement groups aren't linked to a Transaction row —
                // Tier1/2 auto-confirmed matches already posted their voucher when created;
                // this covers Tier3 (aggregated) matches, which always require a human to
                // confirm before the GL entries are posted.
                var posted = await ReconCanon.PosSettlementPoster.PostAsync(db, group, ct);
                if (!posted && !group.IsJournalPosted)
                {
                    db.ReconciliationEvents.Add(new ReconciliationEvent
                    {
                        ReconciliationEventId = Guid.NewGuid(),
                        ReconciliationMatchGroupId = group.ReconciliationMatchGroupId,
                        EventType = "PostingFailed",
                        MatchLevel = group.MatchLevel,
                        Details = "Confirmed but failed to post journal voucher (metadata missing or entries didn't balance).",
                        CreatedAt = now,
                    });
                }
            }
            else
            {
                // Find any transaction in NeedsBankMatch whose card cashout amount and date
                // correspond to this match group and promote it.
                await ReconCanon.CardCashoutPromoter.PromoteLinkedTransactionAsync(db, group, confirmedByUserId, now, ct);
            }

            await db.SaveChangesAsync(ct);
            return true;
        }

        /// <summary>
        /// Rejects a pending match group. Reverts any linked NeedsBankMatch transaction back
        /// to the NeedsBankMatch state (it is already there — but we update Status for clarity).
        /// </summary>
        public async Task<bool> RejectMatchAsync(
            TenantDbContext db, Guid matchGroupId, string rejectionReason,
            Guid rejectedByUserId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(rejectionReason))
            {
                throw new InvalidOperationException("Rejection reason is required.");
            }

            var group = await db.ReconciliationMatchGroups
                .Include(g => g.MatchedRecords)
                .FirstOrDefaultAsync(g => g.ReconciliationMatchGroupId == matchGroupId, ct);

            if (group == null)
            {
                return false;
            }

            group.Status = "Rejected";

            // Log a reconciliation event for the rejection.
            db.ReconciliationEvents.Add(new ReconciliationEvent
            {
                ReconciliationEventId = Guid.NewGuid(),
                ReconciliationMatchGroupId = matchGroupId,
                EventType = "MatchRejected",
                MatchLevel = group.MatchLevel,
                Details = $"Rejected by user {rejectedByUserId}: {rejectionReason}",
                CreatedAt = DateTime.UtcNow,
            });

            // Unmark the imported records so they can be re-matched.
            var recordIds = group.MatchedRecords
                .Select(mr => mr.ImportedNormalizedRecordId)
                .ToList();

            var importedRecords = await db.ImportedNormalizedRecords
                .Where(r => recordIds.Contains(r.ImportedNormalizedRecordId))
                .ToListAsync(ct);

            foreach (var record in importedRecords)
            {
                record.MatchStatus = "PENDING";
            }

            await db.SaveChangesAsync(ct);
            return true;
        }

        /// <summary>
        /// Returns unmatched BANK records (for Expense Match / Rule 4) with contextual hints.
        ///
        /// Key UX feature: if there is no approved card cashout to match a bank line BUT there
        /// IS an unapproved (Pending) card cashout with a similar amount and date, the hint tells
        /// the user to approve that transaction first.
        /// </summary>
        public async Task<List<UnmatchedQueueItem>> GetUnmatchedQueueAsync(
            TenantDbContext db, string? matchRule, DateTime? from, DateTime? to, CancellationToken ct)
        {
            // Currently scoped to Expense Match (Rule 4): BANK records with no match group.
            // Future: extend to other rules via the matchRule parameter.

            // 1. Find all BANK records that are still PENDING (no match group linked).
            var bankQuery = from record in db.ImportedNormalizedRecords.AsNoTracking()
                            join batch in db.ImportBatches.AsNoTracking()
                                on record.ImportBatchId equals batch.ImportBatchId
                            where batch.SourceType == "BANK"
                               && batch.Status == "COMMITTED"
                               && record.MatchStatus == "PENDING"
                            select record;

            if (from.HasValue)
            {
                bankQuery = bankQuery.Where(r => r.TransactionDate >= from.Value);
            }

            if (to.HasValue)
            {
                bankQuery = bankQuery.Where(r => r.TransactionDate <= to.Value);
            }

            var unmatchedBankRecords = await bankQuery
                .OrderBy(r => r.TransactionDate)
                .ToListAsync(ct);

            if (unmatchedBankRecords.Count == 0)
            {
                return new List<UnmatchedQueueItem>();
            }

            // 2. Load all Pending card cashout transactions for the hint-matching lookup.
            var pendingCardCashouts = await db.Transactions
                .AsNoTracking()
                .Where(t =>
                    t.TransactionType == TransactionType.CashOut &&
                    t.PaymentMethod == PaymentMethod.Card &&
                    (t.TransactionState == TransactionState.Pending ||
                     t.TransactionState == TransactionState.NeedsBankMatch))
                .ToListAsync(ct);

            // 3. For each unmatched BANK record, determine the most helpful hint.
            var result = new List<UnmatchedQueueItem>(unmatchedBankRecords.Count);

            foreach (var bankRecord in unmatchedBankRecords)
            {
                var hint = BuildHint(bankRecord, pendingCardCashouts);

                result.Add(new UnmatchedQueueItem(
                    bankRecord.ImportedNormalizedRecordId,
                    "BANK",
                    "Expense",
                    bankRecord.NetAmount,
                    bankRecord.TransactionDate,
                    bankRecord.ReferenceNumber,
                    UnmatchedReasonFor(hint),
                    hint));
            }

            return result;
        }

        public async Task<MatcherSummary> GetSummaryAsync(TenantDbContext db, CancellationToken ct)
        {
            // 1. Pending Confirmations
            var pendingConfirmations = await db.ReconciliationMatchGroups
                .Where(g => !g.IsConfirmed)
                .GroupBy(g => g.MatchLevel)
                .Select(g => new { Level = g.Key, Count = g.Count() })
                .ToListAsync(ct);

            // 2. Exceptions (Variance events)
            var exceptions = await db.ReconciliationEvents
                .Where(e => e.EventType == "Variance")
                .GroupBy(e => e.MatchLevel)
                .Select(g => new { Level = g.Key, Count = g.Count() })
                .ToListAsync(ct);

            // 3. Unmatched (For now, just tracking PENDING bank records for Rule 4,
            // but we can extend this to count MatchNotFound events per level)
            var unmatchedEvents = await db.ReconciliationEvents
                .Where(e => e.EventType == "MatchNotFound")
                .GroupBy(e => e.MatchLevel)
                .Select(g => new { Level = g.Key, Count = g.Count() })
                .ToListAsync(ct);

            var bankUnmatchedCount = await db.ImportedNormalizedRecords
                .Join(db.ImportBatches, r => r.ImportBatchId, b => b.ImportBatchId, (r, b) => new { r, b })
                .Where(x => x.b.SourceType == "BANK" && x.b.Status == "COMMITTED" && x.r.MatchStatus == "PENDING")
                .CountAsync(ct);

            var ruleSummaries = new List<RuleSummary>();
            var levels = new[] { "Level1", "Level2", "Level3", "Level4", "Level5", "Level6" };

            foreach (var level in levels)
            {
                var pCount = pendingConfirmations.FirstOrDefault(x => x.Level == level)?.Count ?? 0;
                var eCount = exceptions.FirstOrDefault(x => x.Level == level)?.Count ?? 0;
                
                var uCount = unmatchedEvents.FirstOrDefault(x => x.Level == level)?.Count ?? 0;
                if (level == "Level4") 
                {
                    // Rule 4 unmatched relies on bank queue currently
                    uCount += bankUnmatchedCount;
                }

                ruleSummaries.Add(new RuleSummary(level, pCount, eCount, uCount));
            }

            return new MatcherSummary(
                ruleSummaries.Sum(r => r.PendingConfirmations),
                ruleSummaries.Sum(r => r.Exceptions),
                ruleSummaries.Sum(r => r.Unmatched),
                ruleSummaries);
        }

        // ── Private helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Amount tolerance for hint matching: ±1% of the bank amount.
        /// </summary>
        private static bool AmountsCloseEnough(decimal bankAmount, decimal txnAmount)
        {
            if (bankAmount == 0) return txnAmount == 0;
            var pctDiff = Math.Abs(bankAmount - txnAmount) / bankAmount;
            return pctDiff <= 0.01m; // 1% tolerance
        }

        private static UnmatchedHint? BuildHint(
            ImportedNormalizedRecord bankRecord,
            List<Transaction> candidates)
        {
            // Check whether any candidate matches amount ± 1% and date ± 1 day.
            foreach (var txn in candidates)
            {
                var dateDiff = Math.Abs((txn.TransactionDate.Date - bankRecord.TransactionDate.Date).TotalDays);
                if (dateDiff > 1 || !AmountsCloseEnough(bankRecord.NetAmount, txn.Amount))
                {
                    continue;
                }

                if (txn.TransactionState == TransactionState.NeedsBankMatch)
                {
                    // Already approved and in matching queue — matcher will process it shortly.
                    return new UnmatchedHint(
                        "MatchingItemNeedsBankMatch",
                        txn.TransactionId,
                        txn.Description,
                        txn.Amount,
                        "A matching approved card expense is already in the bank-match queue. " +
                        "The matcher will link these records on the next run.");
                }

                if (txn.TransactionState == TransactionState.Pending)
                {
                    // There IS a matching expense but it hasn't been approved yet.
                    return new UnmatchedHint(
                        "MatchingItemPendingApproval",
                        txn.TransactionId,
                        txn.Description,
                        txn.Amount,
                        $"⚠ There is an unapproved card expense ({txn.Amount:N2} on " +
                        $"{txn.TransactionDate:yyyy-MM-dd}) that matches this bank entry. " +
                        "Have a manager approve it to unlock reconciliation.");
                }
            }

            // No matching transaction found at all.
            return new UnmatchedHint(
                "NoPossibleMatch",
                null, null, null,
                "No card expense found matching this bank entry. " +
                "The transaction may need to be created manually.");
        }

        private static string UnmatchedReasonFor(UnmatchedHint? hint) =>
            hint?.HintType switch
            {
                "MatchingItemNeedsBankMatch"   => "Awaiting matcher",
                "MatchingItemPendingApproval"  => "Matching expense not yet approved",
                "NoPossibleMatch"              => "No matching expense found",
                _                              => "Unknown"
            };
    }
}
