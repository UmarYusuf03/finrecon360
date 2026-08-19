using finrecon360_backend.Data;
using finrecon360_backend.Models;
using finrecon360_backend.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ReconCanon = finrecon360_backend.Services.Reconciliation;

namespace finrecon360_backend.Services.Workers
{
    public record PosSettlementMatchResult(
        int TotalCandidates,
        int Tier1Matched,
        int Tier2Matched,
        int Tier3Matched,
        int ExceptionCount,
        int NoMatchCount);

    /// <summary>
    /// Implements Level7 (POS-terminal batch settlement): POS_SETTLEMENT batches ↔ BANK deposits.
    ///
    /// POS_SETTLEMENT is the card-terminal/acquirer batch-close file — distinct from the POS
    /// source type, which is the POS software's own EOD sales report used by Level1
    /// (OperationalMatchWorker) to check staff entries. A modern POS setup produces both as
    /// separate files: the register's sales log has no visibility into how the acquirer batched
    /// and settled card charges, so BatchNumber/TerminalId/MerchantId can only come from the
    /// terminal/acquirer's own settlement export.
    ///
    /// Both sides carry BatchNumber/TerminalId/MerchantId — either mapped directly from
    /// structured columns in the POS_SETTLEMENT file, or (for BANK, whose narrative is
    /// unstructured) extracted from the bank description at import time by PosIdentifierExtractor
    /// (see ImportsController.Commit), normalized (trimmed, uppercased, leading zeros stripped
    /// from BatchNumber).
    ///
    /// Matching runs as a four-tier waterfall each cycle, each tier operating only on records
    /// the previous tier left PENDING:
    ///   Tier1 — group both sides by BatchNumber, sum NetAmount, compare within tolerance and the
    ///           T+N business-day settlement window. Auto-confirms and posts on success.
    ///   Tier2 — POS grouped by TerminalId + calendar date, BANK by TerminalId alone. Auto-confirms.
    ///   Tier3 — POS grouped by MerchantId + calendar date (the "many POS batches, one lumped
    ///           bank deposit" scenario), BANK grouped by MerchantId alone (its date won't align
    ///           with the POS date due to settlement lag — the window check reconciles that).
    ///           Never auto-confirms; always routed to human review.
    ///   Tier4 — anything still PENDING gets a MatchNotFound event.
    ///
    /// Cardinality (1:1, many:1 consolidated, 1:many split-network settlement) is not three
    /// separate code paths — it falls out of how many records land in the POS group vs. the BANK
    /// group for a given key. A Visa/Amex split of one POS batch, for example, still shares one
    /// BatchNumber across the two BANK deposit lines, so Tier1's grouping naturally aggregates them.
    ///
    /// A key match whose amounts don't reconcile within tolerance is treated as a distinct
    /// exception for that specific batch (Variance event, MatchStatus=Exception) rather than
    /// falling through to a looser tier — letting a specifically-identified discrepancy get
    /// blended into a broader aggregate at Tier3 would risk masking it with unrelated batches.
    /// </summary>
    public class PosSettlementMatchWorker
    {
        private readonly ILogger<PosSettlementMatchWorker> _logger;
        private readonly IReconciliationSettingsProvider _settingsProvider;

        public PosSettlementMatchWorker(ILogger<PosSettlementMatchWorker> logger, IReconciliationSettingsProvider settingsProvider)
        {
            _logger = logger;
            _settingsProvider = settingsProvider;
        }

        public async Task<PosSettlementMatchResult> ExecuteAsync(
            Guid tenantId,
            TenantDbContext tenantDb,
            CancellationToken ct = default)
        {
            var settings = await _settingsProvider.GetAsync(tenantDb, ct);
            var amountTolerance = settings.AmountTolerance;
            var windowDays = settings.SettlementDateWindowDays;

            var holidays = (await tenantDb.BankingHolidays.AsNoTracking().Select(h => h.Date).ToListAsync(ct))
                .ToHashSet();

            var posRecords = await QueryPendingAsync(tenantDb, "POS_SETTLEMENT", ct);
            var bankRecords = await QueryPendingAsync(tenantDb, "BANK", ct);

            var totalCandidates = posRecords.Count;
            if (posRecords.Count == 0)
            {
                return new PosSettlementMatchResult(totalCandidates, 0, 0, 0, 0, 0);
            }

            // Note: bankRecords may legitimately be empty (e.g. the day's POS batches closed but
            // the bank statement hasn't been imported yet) — still fall through so those POS
            // records get a MatchNotFound event at Tier4 instead of being silently skipped.

            _logger.LogInformation(
                "PosSettlementMatchWorker: tenant {TenantId} — {PosCount} POS records, {BankCount} BANK records",
                tenantId, posRecords.Count, bankRecords.Count);

            var remainingPos = new List<ImportedNormalizedRecord>(posRecords);
            var remainingBank = new List<ImportedNormalizedRecord>(bankRecords);
            var now = DateTime.UtcNow;
            var exceptions = 0;

            // Tier1: exact BatchNumber match on both sides.
            var tier1Matched = await MatchGroupsAsync(
                tenantDb,
                GroupByKey(remainingPos, r => r.BatchNumber),
                GroupByKey(remainingBank, r => r.BatchNumber),
                ReconciliationTiers.Tier1ExactBatch,
                autoConfirm: true,
                amountTolerance, windowDays, holidays,
                remainingPos, remainingBank, now, ct, exceptionCounter: () => exceptions++);

            // Tier2: TerminalId + calendar date on the POS side, for records Tier1 didn't touch.
            // BANK is grouped by TerminalId alone — its date won't align with the POS date due
            // to settlement lag, so the window check at comparison time reconciles the two date
            // domains, not the grouping key. Deliberately TerminalId-only (no MerchantId
            // fallback here): MerchantId is strictly broader — a merchant can have many
            // terminals — so it's reserved for Tier3, keeping the specificity ordering
            // BatchNumber > TerminalId > MerchantId intact instead of two tiers overlapping.
            var tier2Matched = await MatchAsymmetricTierAsync(
                tenantDb, remainingPos, remainingBank,
                posKeySelector: r => r.TerminalId is null ? null : $"{r.TerminalId}|{r.TransactionDate:yyyy-MM-dd}",
                bankKeySelector: r => r.TerminalId,
                ReconciliationTiers.Tier2TerminalOrMerchant,
                autoConfirm: true,
                amountTolerance, windowDays, holidays, now, ct, exceptionCounter: () => exceptions++);

            // Tier3: POS grouped by MerchantId + calendar date only (the last-resort, broadest
            // tier — reached by records with no BatchNumber/TerminalId, or where Tier1/2 keys
            // didn't reconcile); BANK grouped by MerchantId alone. Never auto-confirms — always
            // routed to human review.
            var tier3Matched = await MatchAsymmetricTierAsync(
                tenantDb, remainingPos, remainingBank,
                posKeySelector: r => r.MerchantId is null ? null : $"{r.MerchantId}|{r.TransactionDate:yyyy-MM-dd}",
                bankKeySelector: r => r.MerchantId,
                ReconciliationTiers.Tier3Aggregated,
                autoConfirm: false,
                amountTolerance, windowDays, holidays, now, ct, exceptionCounter: () => exceptions++);

            // Tier4: whatever is left gets a MatchNotFound event; stays PENDING for a later cycle.
            var noMatch = EmitNoMatchEvents(tenantDb, remainingPos, remainingBank, now);

            await tenantDb.SaveChangesAsync(ct);

            _logger.LogInformation(
                "PosSettlementMatchWorker done — tier1={T1}, tier2={T2}, tier3={T3}, exceptions={E}, noMatch={N}",
                tier1Matched, tier2Matched, tier3Matched, exceptions, noMatch);

            return new PosSettlementMatchResult(totalCandidates, tier1Matched, tier2Matched, tier3Matched, exceptions, noMatch);
        }

        // ── Tier1/Tier2 (identical key on both sides) ───────────────────────────────────

        private async Task<int> MatchGroupsAsync(
            TenantDbContext tenantDb,
            Dictionary<string, List<ImportedNormalizedRecord>> posGroups,
            Dictionary<string, List<ImportedNormalizedRecord>> bankGroups,
            string ruleTriggered,
            bool autoConfirm,
            decimal tolerance,
            int windowDays,
            IReadOnlySet<DateOnly> holidays,
            List<ImportedNormalizedRecord> remainingPos,
            List<ImportedNormalizedRecord> remainingBank,
            DateTime now,
            CancellationToken ct,
            Action exceptionCounter)
        {
            var matchedCount = 0;

            foreach (var (key, posGroup) in posGroups)
            {
                if (posGroup.Count == 0 || !bankGroups.TryGetValue(key, out var bankCandidates) || bankCandidates.Count == 0)
                {
                    continue;
                }

                var windowed = FilterWithinWindow(posGroup, bankCandidates, windowDays, holidays);
                if (windowed.Count == 0)
                {
                    // Key matched, but nothing has landed within the settlement window yet — this
                    // isn't a discrepancy, just "not settled yet". Leave both sides untouched so a
                    // looser tier (or a later cycle, once the deposit arrives) can still find it.
                    continue;
                }

                var match = TryBuildAggregateMatch(posGroup, windowed, tolerance);
                if (match is null)
                {
                    // A candidate landed within the window under the same key, but the amount
                    // doesn't reconcile — a genuine exception for this specific batch, not
                    // something a looser tier should be allowed to blend into a broader aggregate.
                    EmitVarianceEvent(tenantDb, key, posGroup, windowed, ruleTriggered, now);
                    foreach (var r in posGroup) r.MatchStatus = ReconCanon.MatchStatuses.Exception;
                    foreach (var r in windowed) r.MatchStatus = ReconCanon.MatchStatuses.Exception;
                    RemoveAll(remainingPos, posGroup);
                    RemoveAll(remainingBank, windowed);
                    exceptionCounter();
                    continue;
                }

                await CreateMatchGroupAsync(tenantDb, match.Value, ruleTriggered, autoConfirm, now, ct);
                RemoveAll(remainingPos, match.Value.PosRecords);
                RemoveAll(remainingBank, match.Value.BankRecords);
                matchedCount++;
            }

            return matchedCount;
        }

        // ── Tier2/Tier3 (asymmetric keys — POS is date-scoped, BANK isn't) ──────────────
        //
        // Both tiers share the same shape: the POS-side key includes a calendar date component
        // (so a merchant/terminal's unmatched history doesn't all get blindly summed together —
        // only records from the same day are candidates for one aggregate), while the BANK-side
        // key is the identifier alone, since settlement lag means its date won't equal the POS
        // date — the window check at comparison time is what reconciles that, not the grouping.
        // They differ only in which identifier they key on (TerminalId-preferring vs.
        // MerchantId-only) and whether a successful match auto-confirms.

        private async Task<int> MatchAsymmetricTierAsync(
            TenantDbContext tenantDb,
            List<ImportedNormalizedRecord> remainingPos,
            List<ImportedNormalizedRecord> remainingBank,
            Func<ImportedNormalizedRecord, string?> posKeySelector,
            Func<ImportedNormalizedRecord, string?> bankKeySelector,
            string ruleTriggered,
            bool autoConfirm,
            decimal tolerance,
            int windowDays,
            IReadOnlySet<DateOnly> holidays,
            DateTime now,
            CancellationToken ct,
            Action exceptionCounter)
        {
            var posGroups = GroupByKey(remainingPos, posKeySelector);
            var bankGroups = GroupByKey(remainingBank, bankKeySelector);

            var matchedCount = 0;

            foreach (var (compositeKey, posGroup) in posGroups)
            {
                if (posGroup.Count == 0)
                {
                    continue;
                }

                var identityKey = compositeKey[..compositeKey.IndexOf('|')];
                if (!bankGroups.TryGetValue(identityKey, out var bankCandidates) || bankCandidates.Count == 0)
                {
                    continue;
                }

                var windowed = FilterWithinWindow(posGroup, bankCandidates, windowDays, holidays);
                if (windowed.Count == 0)
                {
                    // Identity matched, but nothing has settled within the window yet — leave
                    // untouched rather than treating "not settled yet" as an exception.
                    continue;
                }

                var match = TryBuildAggregateMatch(posGroup, windowed, tolerance);
                if (match is null)
                {
                    EmitVarianceEvent(tenantDb, compositeKey, posGroup, windowed, ruleTriggered, now);
                    foreach (var r in posGroup) r.MatchStatus = ReconCanon.MatchStatuses.Exception;
                    foreach (var r in windowed) r.MatchStatus = ReconCanon.MatchStatuses.Exception;
                    RemoveAll(remainingPos, posGroup);
                    RemoveAll(remainingBank, windowed);
                    exceptionCounter();
                    continue;
                }

                await CreateMatchGroupAsync(tenantDb, match.Value, ruleTriggered, autoConfirm, now, ct);
                RemoveAll(remainingPos, match.Value.PosRecords);
                RemoveAll(remainingBank, match.Value.BankRecords);
                matchedCount++;
            }

            return matchedCount;
        }

        // ── Shared primitives ────────────────────────────────────────────────────────────

        private readonly record struct AggregateMatch(
            List<ImportedNormalizedRecord> PosRecords,
            List<ImportedNormalizedRecord> BankRecords,
            decimal PosNetSum,
            decimal PosGrossSum,
            decimal PosFeeSum,
            decimal BankNetSum);

        /// <summary>
        /// Filters bankCandidates to those within the settlement window of the POS group's
        /// earliest date. Kept separate from the amount comparison below: "nothing has settled
        /// within the window yet" and "something settled but the amount is wrong" are different
        /// situations — the former should leave the records untouched for a later cycle, the
        /// latter is a genuine exception.
        /// </summary>
        private static List<ImportedNormalizedRecord> FilterWithinWindow(
            List<ImportedNormalizedRecord> posGroup,
            List<ImportedNormalizedRecord> bankCandidates,
            int windowDays,
            IReadOnlySet<DateOnly> holidays)
        {
            var posEarliestDate = posGroup.Min(r => r.TransactionDate);
            return bankCandidates
                .Where(b => ReconCanon.BusinessDayCalculator.IsWithinSettlementWindow(posEarliestDate, b.TransactionDate, windowDays, holidays))
                .ToList();
        }

        /// <summary>
        /// Compares summed NetAmount across an already-window-filtered POS/BANK pair (strict
        /// decimal arithmetic throughout — every amount field in this codebase is `decimal`,
        /// never float/double, so this is exact fixed-point comparison, not binary-floating-point-
        /// approximate). Returns null only when the sums don't reconcile within tolerance.
        /// </summary>
        private static AggregateMatch? TryBuildAggregateMatch(
            List<ImportedNormalizedRecord> posGroup,
            List<ImportedNormalizedRecord> windowedBankCandidates,
            decimal tolerance)
        {
            var posNetSum = posGroup.Sum(r => r.NetAmount);
            var bankNetSum = windowedBankCandidates.Sum(r => r.NetAmount);

            if (Math.Abs(posNetSum - bankNetSum) > tolerance)
            {
                return null;
            }

            var posGrossSum = posGroup.Sum(r => r.GrossAmount ?? r.NetAmount);
            var posFeeSum = posGroup.Sum(r => r.ProcessingFee ?? 0m);

            return new AggregateMatch(posGroup, windowedBankCandidates, posNetSum, posGrossSum, posFeeSum, bankNetSum);
        }

        private async Task CreateMatchGroupAsync(
            TenantDbContext tenantDb,
            AggregateMatch match,
            string ruleTriggered,
            bool autoConfirm,
            DateTime now,
            CancellationToken ct)
        {
            var batchNumbers = DistinctNonNull(match.PosRecords.Concat(match.BankRecords).Select(r => r.BatchNumber));
            var terminalIds = DistinctNonNull(match.PosRecords.Concat(match.BankRecords).Select(r => r.TerminalId));
            var merchantIds = DistinctNonNull(match.PosRecords.Concat(match.BankRecords).Select(r => r.MerchantId));

            var keyPart = batchNumbers.FirstOrDefault() ?? terminalIds.FirstOrDefault() ?? merchantIds.FirstOrDefault()
                ?? match.PosRecords[0].ImportedNormalizedRecordId.ToString("N");
            var settlementKey = $"POS_BANK|{ruleTriggered}|{keyPart}";

            var metadata = new ReconCanon.MatchGroupMetadata
            {
                SettlementKey = settlementKey,
                PosGrossTotal = match.PosGrossSum,
                PosNetTotal = match.PosNetSum,
                PosFeeTotal = match.PosFeeSum,
                BankNetTotal = match.BankNetSum,
                Variance = Math.Abs(match.PosNetSum - match.BankNetSum),
                AutoMatched = autoConfirm,
                RuleTriggered = ruleTriggered,
                MatchedBatchNumbers = batchNumbers.Count > 0 ? batchNumbers : null,
                MatchedTerminalIds = terminalIds.Count > 0 ? terminalIds : null,
                MatchedMerchantIds = merchantIds.Count > 0 ? merchantIds : null,
                PosRecordCount = match.PosRecords.Count,
                BankRecordCount = match.BankRecords.Count,
                MatchedAt = now,
            };

            var group = new ReconciliationMatchGroup
            {
                ReconciliationMatchGroupId = Guid.NewGuid(),
                MatchLevel = MatchLevels.Level7,
                SettlementKey = settlementKey,
                IsConfirmed = autoConfirm,
                ConfirmedAt = autoConfirm ? now : null,
                MatchedAmount = match.PosNetSum,
                Variance = Math.Abs(match.PosNetSum - match.BankNetSum),
                Status = autoConfirm ? "Confirmed" : "Pending",
                MatchMetadataJson = metadata.Serialize(),
                CreatedAt = now,
            };
            tenantDb.ReconciliationMatchGroups.Add(group);

            LinkRecords(tenantDb, group.ReconciliationMatchGroupId, match.PosRecords, "POS_SETTLEMENT", settlementKey, now);
            LinkRecords(tenantDb, group.ReconciliationMatchGroupId, match.BankRecords, "BANK", settlementKey, now);

            if (autoConfirm)
            {
                var posted = await ReconCanon.PosSettlementPoster.PostAsync(tenantDb, group, ct);
                if (!posted)
                {
                    _logger.LogError(
                        "Level7 match group {GroupId} auto-confirmed but failed to post (metadata missing or entries didn't balance)",
                        group.ReconciliationMatchGroupId);
                }
            }

            _logger.LogInformation(
                "Level7 {Status}: {Rule} — {PosCount} POS record(s) <-> {BankCount} BANK record(s), key '{Key}'",
                autoConfirm ? "AUTO-MATCHED" : "PENDING-REVIEW", ruleTriggered, match.PosRecords.Count, match.BankRecords.Count, settlementKey);
        }

        private static void LinkRecords(
            TenantDbContext tenantDb,
            Guid groupId,
            List<ImportedNormalizedRecord> records,
            string sourceType,
            string settlementKey,
            DateTime now)
        {
            foreach (var r in records)
            {
                tenantDb.ReconciliationMatchedRecords.Add(new ReconciliationMatchedRecord
                {
                    ReconciliationMatchedRecordId = Guid.NewGuid(),
                    ReconciliationMatchGroupId = groupId,
                    ImportedNormalizedRecordId = r.ImportedNormalizedRecordId,
                    SourceType = sourceType,
                    MatchAmount = r.NetAmount,
                    LinkedAt = now,
                });
                r.MatchStatus = ReconCanon.MatchStatuses.Matched;
                r.SettlementKey = settlementKey;
            }
        }

        private static void EmitVarianceEvent(
            TenantDbContext tenantDb,
            string key,
            List<ImportedNormalizedRecord> posGroup,
            List<ImportedNormalizedRecord> bankCandidates,
            string ruleTriggered,
            DateTime now)
        {
            var posSum = posGroup.Sum(r => r.NetAmount);
            var bankSum = bankCandidates.Sum(r => r.NetAmount);

            tenantDb.ReconciliationEvents.Add(new ReconciliationEvent
            {
                ReconciliationEventId = Guid.NewGuid(),
                EventType = ReconciliationEventTypes.Variance,
                MatchLevel = MatchLevels.Level7,
                Details = $"{ruleTriggered}: key='{key}' — POS net total {posSum:F2} ({posGroup.Count} record(s)) " +
                          $"vs BANK net total {bankSum:F2} ({bankCandidates.Count} record(s) in window), " +
                          $"delta={Math.Abs(posSum - bankSum):F2}.",
                CreatedAt = now,
            });
        }

        private int EmitNoMatchEvents(
            TenantDbContext tenantDb,
            List<ImportedNormalizedRecord> remainingPos,
            List<ImportedNormalizedRecord> remainingBank,
            DateTime now)
        {
            foreach (var r in remainingPos)
            {
                tenantDb.ReconciliationEvents.Add(new ReconciliationEvent
                {
                    ReconciliationEventId = Guid.NewGuid(),
                    ImportedNormalizedRecordId = r.ImportedNormalizedRecordId,
                    EventType = ReconciliationEventTypes.MatchNotFound,
                    MatchLevel = MatchLevels.Level7,
                    Details = $"POS record (batch={r.BatchNumber ?? "n/a"}, terminal={r.TerminalId ?? "n/a"}, " +
                              $"merchant={r.MerchantId ?? "n/a"}, amount={r.NetAmount:F2}) has no matching BANK deposit yet.",
                    CreatedAt = now,
                });
            }

            // BANK side is logged too (an unexplained deposit is worth surfacing) but not counted
            // in the returned total — NoMatchCount tracks unmatched POS candidates specifically,
            // consistent with TotalCandidates above.
            foreach (var r in remainingBank)
            {
                tenantDb.ReconciliationEvents.Add(new ReconciliationEvent
                {
                    ReconciliationEventId = Guid.NewGuid(),
                    ImportedNormalizedRecordId = r.ImportedNormalizedRecordId,
                    EventType = ReconciliationEventTypes.MatchNotFound,
                    MatchLevel = MatchLevels.Level7,
                    Details = $"BANK record (batch={r.BatchNumber ?? "n/a"}, terminal={r.TerminalId ?? "n/a"}, " +
                              $"merchant={r.MerchantId ?? "n/a"}, amount={r.NetAmount:F2}) has no matching POS batch.",
                    CreatedAt = now,
                });
            }

            return remainingPos.Count;
        }

        private static Dictionary<string, List<ImportedNormalizedRecord>> GroupByKey(
            IEnumerable<ImportedNormalizedRecord> records,
            Func<ImportedNormalizedRecord, string?> keySelector)
        {
            return records
                .Select(r => (Record: r, Key: keySelector(r)))
                .Where(x => !string.IsNullOrWhiteSpace(x.Key))
                .GroupBy(x => x.Key!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Record).ToList(), StringComparer.OrdinalIgnoreCase);
        }

        private static List<string> DistinctNonNull(IEnumerable<string?> values) =>
            values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        private static void RemoveAll(List<ImportedNormalizedRecord> from, List<ImportedNormalizedRecord> toRemove)
        {
            foreach (var r in toRemove)
            {
                from.Remove(r);
            }
        }

        private static Task<List<ImportedNormalizedRecord>> QueryPendingAsync(
            TenantDbContext tenantDb, string sourceType, CancellationToken ct) =>
            (from r in tenantDb.ImportedNormalizedRecords
             join b in tenantDb.ImportBatches on r.ImportBatchId equals b.ImportBatchId
             where b.SourceType == sourceType && b.Status == "COMMITTED" && r.MatchStatus == ReconCanon.MatchStatuses.Pending
             select r).ToListAsync(ct);
    }

    internal static class ReconciliationTiers
    {
        public const string Tier1ExactBatch = "Tier1_ExactBatch";
        public const string Tier2TerminalOrMerchant = "Tier2_TerminalOrMerchant";
        public const string Tier3Aggregated = "Tier3_Aggregated";
    }
}
