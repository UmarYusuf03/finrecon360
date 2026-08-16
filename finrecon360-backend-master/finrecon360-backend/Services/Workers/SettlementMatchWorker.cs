using finrecon360_backend.Data;
using finrecon360_backend.Models;
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
    /// Match logic:
    ///   - Group GATEWAY records (that haven't been settled yet) by SettlementId.
    ///   - Sum the NetAmount of the group (this represents the expected payout).
    ///   - Find a BANK record whose ReferenceNumber matches the SettlementId.
    ///
    /// On exact match → ReconciliationMatchGroup(Level6, IsConfirmed=true) linking all gateway records to the 1 bank record.
    /// On variance    → ReconciliationEvent(Variance) — e.g. unexpected settlement fees.
    /// On no match    → ReconciliationEvent(MatchNotFound) — payout hasn't hit the bank yet.
    /// </summary>
    public class SettlementMatchWorker
    {
        private readonly ILogger<SettlementMatchWorker> _logger;
        private const decimal AmountTolerance = 0.01m;

        public SettlementMatchWorker(ILogger<SettlementMatchWorker> logger)
        {
            _logger = logger;
        }

        public async Task<MatchingRunResult> ExecuteAsync(
            Guid tenantId,
            TenantDbContext tenantDb,
            CancellationToken ct = default)
        {
            // 1. Load all GATEWAY records that have a SettlementId but are not yet Level6 matched.
            // (They might be Level3 matched to ERP, but they still need Level6 to clear the bank).
            var gatewayRecords = await (
                from r in tenantDb.ImportedNormalizedRecords
                join b in tenantDb.ImportBatches on r.ImportBatchId equals b.ImportBatchId
                where b.SourceType == "GATEWAY" && b.Status == "COMMITTED"
                   && r.MatchStatus != "LEVEL6_MATCHED"
                   && !string.IsNullOrEmpty(r.SettlementId)
                select r
            ).ToListAsync(ct);

            if (gatewayRecords.Count == 0)
            {
                return MatchingRunResult.Empty("Level6");
            }

            // Group by SettlementId to calculate expected bulk payout amounts.
            var gatewayBySettlement = gatewayRecords
                .GroupBy(r => r.SettlementId!.Trim().ToUpperInvariant())
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            // 2. Load COMMITTED BANK records that are PENDING a match.
            var bankRecords = await (
                from r in tenantDb.ImportedNormalizedRecords
                join b in tenantDb.ImportBatches on r.ImportBatchId equals b.ImportBatchId
                where b.SourceType == "BANK" && b.Status == "COMMITTED" && r.MatchStatus == "PENDING"
                select r
            ).ToListAsync(ct);

            _logger.LogInformation(
                "SettlementMatchWorker: tenant {TenantId} — {GwGroups} GATEWAY settlement groups, {BankCount} BANK records",
                tenantId, gatewayBySettlement.Count, bankRecords.Count);

            // 3. Build BANK lookup by ReferenceNumber (assuming Settlement ID is passed in the bank reference).
            var bankByRef = bankRecords
                .Where(r => !string.IsNullOrEmpty(r.ReferenceNumber))
                .GroupBy(r => r.ReferenceNumber!.Trim().ToUpperInvariant())
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            var autoMatched = 0;
            var exceptions = 0;
            var noMatch = 0;
            var now = DateTime.UtcNow;

            foreach (var kvp in gatewayBySettlement)
            {
                var settlementId = kvp.Key;
                var groupRecords = kvp.Value;
                var expectedPayout = groupRecords.Sum(r => r.NetAmount);

                if (!bankByRef.TryGetValue(settlementId, out var bankCandidates))
                {
                    // No bank deposit found for this settlement ID.
                    // This is common if the gateway settles T+2 and the bank statement is from today.
                    // We only log this as a standard MatchNotFound (it will sit in the queue until the bank file arrives).
                    tenantDb.ReconciliationEvents.Add(new ReconciliationEvent
                    {
                        ReconciliationEventId = Guid.NewGuid(),
                        EventType = "MatchNotFound",
                        MatchLevel = "Level6",
                        Details = $"Settlement {settlementId} (expected {expectedPayout:F2} from {groupRecords.Count} records) " +
                                  $"not yet found on Bank Statement.",
                        CreatedAt = now,
                    });
                    noMatch++;
                    continue;
                }

                // Find the bank deposit that matches the expected payout sum.
                var matchedBankRecord = bankCandidates.FirstOrDefault(b =>
                    Math.Abs(b.NetAmount - expectedPayout) < AmountTolerance);

                if (matchedBankRecord == null)
                {
                    // Bank deposit found with matching Settlement ID, but amount differs.
                    var best = bankCandidates[0];
                    tenantDb.ReconciliationEvents.Add(new ReconciliationEvent
                    {
                        ReconciliationEventId = Guid.NewGuid(),
                        EventType = "Variance",
                        MatchLevel = "Level6",
                        Details = $"Settlement {settlementId}: Gateway expects {expectedPayout:F2}, " +
                                  $"but Bank received {best.NetAmount:F2} (delta {Math.Abs(best.NetAmount - expectedPayout):F2}). " +
                                  $"Possible undisclosed settlement fees.",
                        CreatedAt = now,
                    });
                    exceptions++;
                    continue;
                }

                // Exact match — create Level6 group linking ALL gateway records + the 1 bank record.
                var settlementKey = $"SETTLEMENT|{settlementId}";
                var matchGroup = new ReconciliationMatchGroup
                {
                    ReconciliationMatchGroupId = Guid.NewGuid(),
                    MatchLevel = "Level6",
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

                matchedBankRecord.MatchStatus = "MATCHED";
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

                    gw.MatchStatus = "LEVEL6_MATCHED";
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

            return new MatchingRunResult("Level6", gatewayBySettlement.Count, autoMatched, exceptions, noMatch);
        }
    }
}
