using finrecon360_backend.Data;
using finrecon360_backend.Models;
using finrecon360_backend.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace finrecon360_backend.Services.Workers
{
    /// <summary>
    /// Implements Settlement Match (Rule 6): Gateway Payout ↔ Bank Statement.
    ///
    /// Match Key: Settlement ID
    ///
    /// Purpose: Matches a consolidated payout from the Payment Gateway (which groups
    /// many individual sales) to a single bulk deposit on the Bank Statement.
    /// Ensures that all processed funds actually arrive in the business bank account.
    ///
    /// Source A (Internal): ImportedNormalizedRecords from GATEWAY grouped by SettlementId
    /// Source B (External): ImportedNormalizedRecords from BANK where Reference = SettlementId
    ///
    /// NOTE on bank-account scoping: unlike Level4/Level5, a GATEWAY settlement record has no
    /// field identifying which of the tenant's bank accounts it should land in — the settlement
    /// reference is the only anchor. Matching here therefore still searches BANK records
    /// tenant-wide; scoping would require the gateway source to carry an account hint, which it
    /// doesn't today.
    ///
    /// Match logic:
    ///   - Group GATEWAY records (that haven't been settled yet) by SettlementId.
    ///   - Sum the NetAmount of the group (this represents the expected payout).
    ///   - Find a BANK record whose ReferenceNumber matches the SettlementId.
    ///
    /// Bank-account scoping: mirrors Level4's pattern (BankStatementReconciliationWorker) —
    /// if the GATEWAY import batch was uploaded with a BankAccountId (the account that
    /// gateway's payouts settle to), BANK candidates are filtered to that account first.
    /// A null on either side (batch uploaded without picking an account) falls through to
    /// the old tenant-wide search rather than excluding everything, so existing batches
    /// uploaded before this field was populated keep working exactly as before.
    ///
    /// On exact match → ReconciliationMatchGroup(Level6, IsConfirmed=true) linking all gateway records to the 1 bank record.
    /// On ambiguous match (2+ BANK candidates within tolerance of each other) → ReconciliationEvent(RequiresReview).
    /// On variance    → ReconciliationEvent(Variance) — e.g. unexpected settlement fees.
    /// On no match    → ReconciliationEvent(MatchNotFound) — payout hasn't hit the bank yet.
    /// </summary>
    public class SettlementMatchWorker
    {
        private readonly ILogger<SettlementMatchWorker> _logger;
        private readonly IReconciliationSettingsProvider _settingsProvider;

        public SettlementMatchWorker(ILogger<SettlementMatchWorker> logger, IReconciliationSettingsProvider settingsProvider)
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

            // 1. Load all GATEWAY records that have a SettlementId but are not yet Level6 matched.
            // (They might be Level3 matched to ERP, but they still need Level6 to clear the bank).
            // BatchBankAccountId travels with each record so a settlement group can be scoped to
            // the account its gateway batch was uploaded against.
            var gatewayRecordsWithAccount = await (
                from r in tenantDb.ImportedNormalizedRecords
                join b in tenantDb.ImportBatches on r.ImportBatchId equals b.ImportBatchId
                where b.SourceType == "GATEWAY" && b.Status == "COMMITTED"
                   && r.MatchStatus != MatchStatuses.Level6Matched
                   && !string.IsNullOrEmpty(r.SettlementId)
                select new { Record = r, BatchBankAccountId = b.BankAccountId }
            ).ToListAsync(ct);

            if (gatewayRecordsWithAccount.Count == 0)
            {
                return MatchingRunResult.Empty(MatchLevels.Level6);
            }

            // Group by SettlementId to calculate expected bulk payout amounts. A settlement
            // group's account is the first non-null BatchBankAccountId seen in it — one gateway
            // batch upload carries one account, so every record in the group agrees.
            var gatewayBySettlement = gatewayRecordsWithAccount
                .GroupBy(x => x.Record.SettlementId!.Trim().ToUpperInvariant(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => (Records: g.Select(x => x.Record).ToList(), BankAccountId: g.Select(x => x.BatchBankAccountId).FirstOrDefault(id => id.HasValue)),
                    StringComparer.OrdinalIgnoreCase);

            // 2. Load COMMITTED BANK records that are PENDING a match.
            var bankRecordsWithAccount = await (
                from r in tenantDb.ImportedNormalizedRecords
                join b in tenantDb.ImportBatches on r.ImportBatchId equals b.ImportBatchId
                where b.SourceType == "BANK" && b.Status == "COMMITTED" && r.MatchStatus == MatchStatuses.Pending
                select new { Record = r, BatchBankAccountId = b.BankAccountId }
            ).ToListAsync(ct);

            _logger.LogInformation(
                "SettlementMatchWorker: tenant {TenantId} — {GwGroups} GATEWAY settlement groups, {BankCount} BANK records",
                tenantId, gatewayBySettlement.Count, bankRecordsWithAccount.Count);

            // 3. Build BANK lookup by ReferenceNumber (assuming Settlement ID is passed in the bank reference).
            var bankByRef = bankRecordsWithAccount
                .Where(x => !string.IsNullOrEmpty(x.Record.ReferenceNumber))
                .GroupBy(x => x.Record.ReferenceNumber!.Trim().ToUpperInvariant(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(x => (x.Record, x.BatchBankAccountId)).ToList(),
                    StringComparer.OrdinalIgnoreCase);

            var autoMatched = 0;
            var exceptions = 0;
            var noMatch = 0;
            var now = DateTime.UtcNow;

            foreach (var kvp in gatewayBySettlement)
            {
                var settlementId = kvp.Key;
                var groupRecords = kvp.Value.Records;
                var gatewayBankAccountId = kvp.Value.BankAccountId;
                var expectedPayout = groupRecords.Sum(r => r.NetAmount);

                if (!bankByRef.TryGetValue(settlementId, out var bankCandidatesWithAccount))
                {
                    // No bank deposit found for this settlement ID.
                    // This is common if the gateway settles T+2 and the bank statement is from today.
                    // We only log this as a standard MatchNotFound (it will sit in the queue until the bank file arrives).
                    tenantDb.ReconciliationEvents.Add(new ReconciliationEvent
                    {
                        ReconciliationEventId = Guid.NewGuid(),
                        EventType = ReconciliationEventTypes.MatchNotFound,
                        MatchLevel = MatchLevels.Level6,
                        Details = $"Settlement {settlementId} (expected {expectedPayout:F2} from {groupRecords.Count} records) " +
                                  $"not yet found on Bank Statement.",
                        CreatedAt = now,
                    });
                    noMatch++;
                    continue;
                }

                // Scope to the gateway batch's bank account when both sides have one set; a null
                // on either side (batch uploaded without picking an account) falls through to the
                // old tenant-wide behavior instead of excluding everything.
                var bankCandidates = bankCandidatesWithAccount
                    .Where(x => gatewayBankAccountId == null || x.BatchBankAccountId == null || x.BatchBankAccountId == gatewayBankAccountId)
                    .Select(x => x.Record)
                    .ToList();

                if (bankCandidates.Count == 0)
                {
                    tenantDb.ReconciliationEvents.Add(new ReconciliationEvent
                    {
                        ReconciliationEventId = Guid.NewGuid(),
                        EventType = ReconciliationEventTypes.MatchNotFound,
                        MatchLevel = MatchLevels.Level6,
                        Details = $"Settlement {settlementId} (expected {expectedPayout:F2} from {groupRecords.Count} records): " +
                                  $"a BANK deposit with this reference exists but not on the gateway's bank account.",
                        CreatedAt = now,
                    });
                    noMatch++;
                    continue;
                }

                // Find the bank deposit closest to the expected payout sum; flag ambiguity
                // instead of silently taking the first one when 2+ candidates are within
                // tolerance of each other.
                var matchResult = AmountMatcher.FindBestMatch(bankCandidates, expectedPayout, amountTolerance);

                if (matchResult.Outcome == AmountMatcher.Outcome.Ambiguous)
                {
                    tenantDb.ReconciliationEvents.Add(new ReconciliationEvent
                    {
                        ReconciliationEventId = Guid.NewGuid(),
                        EventType = ReconciliationEventTypes.RequiresReview,
                        MatchLevel = MatchLevels.Level6,
                        Details = $"Settlement {settlementId}: {matchResult.CandidateCount} BANK deposits with this " +
                                  $"reference are within tolerance of the expected payout ({expectedPayout:F2}).",
                        CreatedAt = now,
                    });
                    exceptions++;
                    continue;
                }

                if (matchResult.Outcome == AmountMatcher.Outcome.NoMatch)
                {
                    // Bank deposit found with matching Settlement ID, but amount differs.
                    var best = bankCandidates
                        .OrderBy(b => Math.Abs(b.NetAmount - expectedPayout))
                        .First();
                    tenantDb.ReconciliationEvents.Add(new ReconciliationEvent
                    {
                        ReconciliationEventId = Guid.NewGuid(),
                        EventType = ReconciliationEventTypes.Variance,
                        MatchLevel = MatchLevels.Level6,
                        Details = $"Settlement {settlementId}: Gateway expects {expectedPayout:F2}, " +
                                  $"but Bank received {best.NetAmount:F2} (delta {Math.Abs(best.NetAmount - expectedPayout):F2}). " +
                                  $"Possible undisclosed settlement fees.",
                        CreatedAt = now,
                    });
                    exceptions++;
                    continue;
                }

                var matchedBankRecord = matchResult.Best!;

                // Exact match — create Level6 group linking ALL gateway records + the 1 bank record.
                var settlementKey = $"SETTLEMENT|{settlementId}";
                var matchGroup = new ReconciliationMatchGroup
                {
                    ReconciliationMatchGroupId = Guid.NewGuid(),
                    MatchLevel = MatchLevels.Level6,
                    SettlementKey = settlementKey,
                    IsConfirmed = true,
                    ConfirmedAt = now,
                    MatchedAmount = expectedPayout,
                    Variance = 0m,
                    Status = "Confirmed",
                    CreatedAt = now,
                };
                tenantDb.ReconciliationMatchGroups.Add(matchGroup);

                // Link the Bank Record
                tenantDb.ReconciliationMatchedRecords.Add(new ReconciliationMatchedRecord
                {
                    ReconciliationMatchedRecordId = Guid.NewGuid(),
                    ReconciliationMatchGroupId = matchGroup.ReconciliationMatchGroupId,
                    ImportedNormalizedRecordId = matchedBankRecord.ImportedNormalizedRecordId,
                    SourceType = "BANK",
                    LinkedAt = now,
                });

                matchedBankRecord.MatchStatus = MatchStatuses.Matched;
                matchedBankRecord.SettlementKey = settlementKey;

                // Link all Gateway Records
                foreach (var gw in groupRecords)
                {
                    tenantDb.ReconciliationMatchedRecords.Add(new ReconciliationMatchedRecord
                    {
                        ReconciliationMatchedRecordId = Guid.NewGuid(),
                        ReconciliationMatchGroupId = matchGroup.ReconciliationMatchGroupId,
                        ImportedNormalizedRecordId = gw.ImportedNormalizedRecordId,
                        SourceType = "GATEWAY",
                        LinkedAt = now,
                    });

                    gw.MatchStatus = MatchStatuses.Level6Matched;
                    gw.SettlementKey = settlementKey;
                }

                _logger.LogInformation(
                    "Level6 AUTO-MATCHED: Settlement {SettlementId} (Gateway {Count} records) ↔ BANK {BankId}",
                    settlementId, groupRecords.Count, matchedBankRecord.ImportedNormalizedRecordId);

                autoMatched++;
            }

            await tenantDb.SaveChangesAsync(ct);

            _logger.LogInformation(
                "SettlementMatchWorker done — matched={M} groups, exceptions={E}, noMatch={N}",
                autoMatched, exceptions, noMatch);

            return new MatchingRunResult(MatchLevels.Level6, gatewayBySettlement.Count, autoMatched, exceptions, noMatch);
        }
    }
}
