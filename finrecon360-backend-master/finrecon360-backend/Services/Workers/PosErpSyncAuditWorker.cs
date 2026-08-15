using finrecon360_backend.Data;
using finrecon360_backend.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace finrecon360_backend.Services.Workers
{
    /// <summary>
    /// Implements Sync Audit (Rule 2): POS EOD Export ↔ ERP Sales Ledger.
    ///
    /// Match Key: Reference Number (Order ID)
    ///
    /// Purpose: Ensures every sale recorded in the POS has been successfully migrated
    /// into the accounting system (ERP). A POS record with no matching ERP entry means
    /// the sales sync job failed or was skipped.
    ///
    /// Source A (Internal): ImportedNormalizedRecords from committed POS batches (Level1-matched or all)
    /// Source B (External): ImportedNormalizedRecords from committed ERP batches
    ///
    /// On exact ref match + amount match → ReconciliationMatchGroup(Level2, IsConfirmed=true)
    /// On ref match but amount delta      → ReconciliationEvent(Variance) — requires review
    /// On no ERP record for ref           → ReconciliationEvent(MatchNotFound) — sync gap
    /// </summary>
    public class PosErpSyncAuditWorker
    {
        private readonly ILogger<PosErpSyncAuditWorker> _logger;
        private const decimal AmountTolerance = 0.01m;

        public PosErpSyncAuditWorker(ILogger<PosErpSyncAuditWorker> logger)
        {
            _logger = logger;
        }

        public async Task<MatchingRunResult> ExecuteAsync(
            Guid tenantId,
            TenantDbContext tenantDb,
            CancellationToken ct = default)
        {
            // 1. Load all COMMITTED POS records (regardless of Level1 match state).
            //    These are the "source of truth" from the POS that should appear in ERP.
            var posRecords = await (
                from r in tenantDb.ImportedNormalizedRecords
                join b in tenantDb.ImportBatches on r.ImportBatchId equals b.ImportBatchId
                where b.SourceType == "POS" && b.Status == "COMMITTED"
                   && r.MatchStatus != "LEVEL2_MATCHED" // skip already Level2-matched
                   && !string.IsNullOrEmpty(r.ReferenceNumber)
                select r
            ).ToListAsync(ct);

            if (posRecords.Count == 0)
            {
                return MatchingRunResult.Empty("Level2");
            }

            // 2. Load all COMMITTED ERP records that are PENDING a Level2 match.
            var erpRecords = await (
                from r in tenantDb.ImportedNormalizedRecords
                join b in tenantDb.ImportBatches on r.ImportBatchId equals b.ImportBatchId
                where b.SourceType == "ERP" && b.Status == "COMMITTED" && r.MatchStatus == "PENDING"
                   && !string.IsNullOrEmpty(r.ReferenceNumber)
                select r
            ).ToListAsync(ct);

            _logger.LogInformation(
                "PosErpSyncAuditWorker: tenant {TenantId} — {PosCount} POS records, {ErpCount} ERP records",
                tenantId, posRecords.Count, erpRecords.Count);

            // 3. Build ERP lookup by ReferenceNumber (case-insensitive).
            var erpByRef = erpRecords
                .GroupBy(r => r.ReferenceNumber!.Trim().ToUpperInvariant())
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            var autoMatched = 0;
            var exceptions = 0;
            var noMatch = 0;
            var now = DateTime.UtcNow;

            foreach (var posRecord in posRecords)
            {
                var refKey = posRecord.ReferenceNumber!.Trim().ToUpperInvariant();

                if (!erpByRef.TryGetValue(refKey, out var erpCandidates))
                {
                    // No ERP record for this POS order — sync gap.
                    tenantDb.ReconciliationEvents.Add(new ReconciliationEvent
                    {
                        ReconciliationEventId = Guid.NewGuid(),
                        ImportedNormalizedRecordId = posRecord.ImportedNormalizedRecordId,
                        EventType = "MatchNotFound",
                        MatchLevel = "Level2",
                        Details = $"POS record ref={posRecord.ReferenceNumber} (amount={posRecord.NetAmount}) " +
                                  $"has no corresponding ERP Sales Ledger entry. Possible sync gap.",
                        CreatedAt = now,
                    });
                    noMatch++;
                    continue;
                }

                // Find ERP candidate with matching amount.
                var erpRecord = erpCandidates.FirstOrDefault(e =>
                    Math.Abs(e.NetAmount - posRecord.NetAmount) < AmountTolerance);

                if (erpRecord == null)
                {
                    // Reference matches but amount is different — possible rounding or discount.
                    var bestCandidate = erpCandidates[0];
                    tenantDb.ReconciliationEvents.Add(new ReconciliationEvent
                    {
                        ReconciliationEventId = Guid.NewGuid(),
                        ImportedNormalizedRecordId = posRecord.ImportedNormalizedRecordId,
                        EventType = "Variance",
                        MatchLevel = "Level2",
                        Details = $"Ref={posRecord.ReferenceNumber}: POS amount={posRecord.NetAmount}, " +
                                  $"ERP amount={bestCandidate.NetAmount}, " +
                                  $"delta={Math.Abs(bestCandidate.NetAmount - posRecord.NetAmount):F2}",
                        CreatedAt = now,
                    });
                    exceptions++;
                    continue;
                }

                // Exact match — create Level2 group.
                var settlementKey = $"POS_ERP|{refKey}";
                var matchGroup = new ReconciliationMatchGroup
                {
                    ReconciliationMatchGroupId = Guid.NewGuid(),
                    MatchLevel = "Level2",
                    SettlementKey = settlementKey,
                    IsConfirmed = true,
                    ConfirmedAt = now,
                    MatchedAmount = posRecord.NetAmount,
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
                tenantDb.ReconciliationMatchedRecords.Add(new ReconciliationMatchedRecord
                {
                    ReconciliationMatchedRecordId = Guid.NewGuid(),
                    ReconciliationMatchGroupId = matchGroup.ReconciliationMatchGroupId,
                    ImportedNormalizedRecordId = erpRecord.ImportedNormalizedRecordId,
                    SourceType = "ERP",
                    LinkedAt = now,
                });

                // Use a distinct status so Level3 worker can pick up ERP records that passed Level2.
                erpRecord.MatchStatus = "MATCHED";
                erpRecord.SettlementKey = settlementKey;
                // POS record gets a level-specific status so it doesn't get re-processed.
                posRecord.MatchStatus = "LEVEL2_MATCHED";
                posRecord.SettlementKey = settlementKey;

                _logger.LogInformation(
                    "Level2 AUTO-MATCHED: POS {PosId} ↔ ERP {ErpId} (ref={Ref})",
                    posRecord.ImportedNormalizedRecordId, erpRecord.ImportedNormalizedRecordId, refKey);

                autoMatched++;
            }

            await tenantDb.SaveChangesAsync(ct);

            _logger.LogInformation(
                "PosErpSyncAuditWorker done — matched={M}, exceptions={E}, noMatch={N}",
                autoMatched, exceptions, noMatch);

            return new MatchingRunResult("Level2", posRecords.Count, autoMatched, exceptions, noMatch);
        }
    }
}
