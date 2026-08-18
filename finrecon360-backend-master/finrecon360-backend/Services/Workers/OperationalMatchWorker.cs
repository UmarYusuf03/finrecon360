using finrecon360_backend.Data;
using finrecon360_backend.Models;
using finrecon360_backend.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace finrecon360_backend.Services.Workers
{
    /// <summary>
    /// Implements Operational Match (Rule 1): Staff Manual Input ↔ POS End-of-Day.
    ///
    /// Match Key: Date + Amount + Reference Number
    ///
    /// Purpose: Ensures that each staff-entered transaction corresponds to exactly one
    /// POS EOD record, confirming no manual entry errors or omissions.
    ///
    /// Source A (Internal): Transactions in JournalReady state (staff cash-in entries)
    /// Source B (External): ImportedNormalizedRecords from committed POS batches
    ///
    /// On exact match   → ReconciliationMatchGroup(Level1, IsConfirmed=true) + both records linked.
    /// On amount delta  → ReconciliationEvent(Variance) — surfaces in unmatched queue.
    /// On ambiguous match (2+ POS candidates within tolerance of each other) → ReconciliationEvent(RequiresReview).
    /// On no POS record → ReconciliationEvent(MatchNotFound) — surfaces in unmatched queue.
    /// </summary>
    public class OperationalMatchWorker
    {
        private readonly ILogger<OperationalMatchWorker> _logger;
        private readonly IReconciliationSettingsProvider _settingsProvider;

        public OperationalMatchWorker(ILogger<OperationalMatchWorker> logger, IReconciliationSettingsProvider settingsProvider)
        {
            _logger = logger;
            _settingsProvider = settingsProvider;
        }

        public async Task<MatchingRunResult> ExecuteAsync(
            Guid tenantId,
            TenantDbContext tenantDb,
            CancellationToken ct = default)
        {
            var settings = await _settingsProvider.GetAsync(tenantDb, ct);
            var amountTolerance = settings.AmountTolerance;

            // 1. Load all JournalReady CashIn transactions that have not yet been Level1-matched.
            //    We identify "not yet matched" by checking whether a Level1 group already links
            //    to a POS record for this transaction's amount + date.
            //    Simple approach: load all JournalReady CashIn, then skip those whose amount+date
            //    already has a Confirmed Level1 group. (Idempotent run.)
            var staffEntries = await tenantDb.Transactions
                .Where(t =>
                    t.TransactionState == TransactionState.JournalReady &&
                    t.TransactionType == TransactionType.CashIn)
                .ToListAsync(ct);

            if (staffEntries.Count == 0)
            {
                return MatchingRunResult.Empty(MatchLevels.Level1);
            }

            // 2. Load all COMMITTED POS records that are still PENDING.
            var posRecords = await (
                from r in tenantDb.ImportedNormalizedRecords
                join b in tenantDb.ImportBatches on r.ImportBatchId equals b.ImportBatchId
                where b.SourceType == "POS" && b.Status == "COMMITTED" && r.MatchStatus == MatchStatuses.Pending
                select r
            ).ToListAsync(ct);

            _logger.LogInformation(
                "OperationalMatchWorker: tenant {TenantId} — {StaffCount} staff entries, {PosCount} POS records",
                tenantId, staffEntries.Count, posRecords.Count);

            // 3. Build POS lookup by (Date, ReferenceNumber) for fast matching.
            //    Key: "YYYY-MM-DD|REF" (case-insensitive ref).
            var posByDateRef = posRecords
                .Where(r => !string.IsNullOrWhiteSpace(r.ReferenceNumber))
                .GroupBy(r => BuildMatchKey(r.TransactionDate, r.ReferenceNumber!))
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            var autoMatched = 0;
            var exceptions = 0;
            var noMatch = 0;
            var now = DateTime.UtcNow;

            foreach (var entry in staffEntries)
            {
                // Skip entries that have no reference number — cannot match without ref.
                if (string.IsNullOrWhiteSpace(entry.Description))
                {
                    // Use description as a fallback ref only when ReferenceNumber isn't tracked on Transaction.
                    // For now skip — Rule 1 requires a reference.
                    noMatch++;
                    continue;
                }

                var matchKey = BuildMatchKey(entry.TransactionDate, entry.Description);

                if (!posByDateRef.TryGetValue(matchKey, out var candidates))
                {
                    // No POS record for this date+ref.
                    tenantDb.ReconciliationEvents.Add(new ReconciliationEvent
                    {
                        ReconciliationEventId = Guid.NewGuid(),
                        ImportedNormalizedRecordId = null,
                        EventType = ReconciliationEventTypes.MatchNotFound,
                        MatchLevel = MatchLevels.Level1,
                        Details = $"No POS record found for staff entry {entry.TransactionId} " +
                                  $"(date={entry.TransactionDate:yyyy-MM-dd}, ref={entry.Description}, amount={entry.Amount})",
                        CreatedAt = now,
                    });
                    noMatch++;
                    continue;
                }

                // Find the candidate with the closest matching amount; flag ambiguity instead of
                // silently taking the first one when 2+ candidates are within tolerance of each other.
                var matchResult = AmountMatcher.FindBestMatch(candidates, entry.Amount, amountTolerance);

                if (matchResult.Outcome == AmountMatcher.Outcome.Ambiguous)
                {
                    tenantDb.ReconciliationEvents.Add(new ReconciliationEvent
                    {
                        ReconciliationEventId = Guid.NewGuid(),
                        ImportedNormalizedRecordId = matchResult.Best!.ImportedNormalizedRecordId,
                        ImportBatchId = matchResult.Best!.ImportBatchId,
                        SourceType = "POS",
                        EventType = ReconciliationEventTypes.RequiresReview,
                        MatchLevel = MatchLevels.Level1,
                        Details = $"Ambiguous match: {matchResult.CandidateCount} POS records share date+ref " +
                                  $"and are within tolerance of staff entry {entry.TransactionId} (amount={entry.Amount}).",
                        CreatedAt = now,
                    });
                    exceptions++;
                    continue;
                }

                if (matchResult.Outcome == AmountMatcher.Outcome.NoMatch)
                {
                    // Date+ref matched but amount differs — variance event.
                    var bestCandidate = candidates[0];
                    tenantDb.ReconciliationEvents.Add(new ReconciliationEvent
                    {
                        ReconciliationEventId = Guid.NewGuid(),
                        ImportedNormalizedRecordId = bestCandidate.ImportedNormalizedRecordId,
                        ImportBatchId = bestCandidate.ImportBatchId,
                        SourceType = "POS",
                        EventType = ReconciliationEventTypes.Variance,
                        MatchLevel = MatchLevels.Level1,
                        Details = $"Amount variance: staff={entry.Amount}, POS={bestCandidate.NetAmount}, " +
                                  $"delta={Math.Abs(bestCandidate.NetAmount - entry.Amount):F2}",
                        CreatedAt = now,
                    });
                    exceptions++;
                    continue;
                }

                var posRecord = matchResult.Best!;

                // Exact match — create Level1 group.
                var settlementKey = BuildMatchKey(entry.TransactionDate, entry.Description);
                var matchGroup = new ReconciliationMatchGroup
                {
                    ReconciliationMatchGroupId = Guid.NewGuid(),
                    MatchLevel = MatchLevels.Level1,
                    SettlementKey = settlementKey,
                    IsConfirmed = true,
                    ConfirmedAt = now,
                    MatchedAmount = entry.Amount,
                    Variance = 0m,
                    Status = "Confirmed",
                    CreatedAt = now,
                };
                tenantDb.ReconciliationMatchGroups.Add(matchGroup);

                tenantDb.ReconciliationMatchedRecords.Add(new ReconciliationMatchedRecord
                {
                    ReconciliationMatchedRecordId = Guid.NewGuid(),
                    ReconciliationMatchGroupId = matchGroup.ReconciliationMatchGroupId,
                    ImportedNormalizedRecordId = posRecord.ImportedNormalizedRecordId,
                    SourceType = "POS",
                    LinkedAt = now,
                });

                posRecord.MatchStatus = MatchStatuses.Matched;
                posRecord.SettlementKey = settlementKey;

                _logger.LogInformation(
                    "Level1 AUTO-MATCHED: staff entry {TxnId} ↔ POS record {RecordId}",
                    entry.TransactionId, posRecord.ImportedNormalizedRecordId);

                autoMatched++;
            }

            await tenantDb.SaveChangesAsync(ct);

            _logger.LogInformation(
                "OperationalMatchWorker done — matched={M}, exceptions={E}, noMatch={N}",
                autoMatched, exceptions, noMatch);

            return new MatchingRunResult(MatchLevels.Level1, staffEntries.Count, autoMatched, exceptions, noMatch);
        }

        private static string BuildMatchKey(DateTime date, string reference) =>
            $"{date:yyyy-MM-dd}|{reference.Trim().ToUpperInvariant()}";
    }
}
