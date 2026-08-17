using finrecon360_backend.Data;
using finrecon360_backend.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace finrecon360_backend.Services.Workers
{
    /// <summary>
    /// Implements Sales Match (Rule 3): ERP Sales Ledger ↔ Payment Gateway.
    ///
    /// Match Key: Reference Number (Order ID)
    ///
    /// Purpose: Proves the customer was actually charged for every sale booked in the ERP.
    /// An ERP record with no matching Gateway charge means revenue was recorded but the
    /// payment was never collected — a critical gap.
    ///
    /// Source A (Internal): ImportedNormalizedRecords from committed ERP batches
    /// Source B (External): ImportedNormalizedRecords from committed GATEWAY batches
    ///
    /// On exact ref + amount match → ReconciliationMatchGroup(Level3, IsConfirmed=true)
    /// On ref match but amount delta → ReconciliationEvent(Variance) — possible partial payment/refund
    /// On no Gateway record          → ReconciliationEvent(MatchNotFound) — revenue not collected
    /// </summary>
    public class ErpGatewaySalesMatchWorker
    {
        private readonly ILogger<ErpGatewaySalesMatchWorker> _logger;
        private const decimal AmountTolerance = 0.01m;

        public ErpGatewaySalesMatchWorker(ILogger<ErpGatewaySalesMatchWorker> logger)
        {
            _logger = logger;
        }

        public async Task<MatchingRunResult> ExecuteAsync(
            Guid tenantId,
            TenantDbContext tenantDb,
            CancellationToken ct = default)
        {
            // 1. Load ERP records: those that passed Level2 (MATCHED) or are still PENDING.
            //    Both cases need Level3 matching — we run Level3 regardless of Level2 status
            //    because some tenants may not have POS and skip Level2.
            var erpRecords = await (
                from r in tenantDb.ImportedNormalizedRecords
                join b in tenantDb.ImportBatches on r.ImportBatchId equals b.ImportBatchId
                where b.SourceType == "ERP" && b.Status == "COMMITTED"
                   && r.MatchStatus != "SALES_VERIFIED" // idempotency guard
                   && !string.IsNullOrEmpty(r.ReferenceNumber)
                select r
            ).ToListAsync(ct);

            if (erpRecords.Count == 0)
            {
                return MatchingRunResult.Empty("Level3");
            }

            // 2. Load all COMMITTED GATEWAY records that are PENDING a match.
            var gatewayRecords = await (
                from r in tenantDb.ImportedNormalizedRecords
                join b in tenantDb.ImportBatches on r.ImportBatchId equals b.ImportBatchId
                where b.SourceType == "GATEWAY" && b.Status == "COMMITTED" && r.MatchStatus == "PENDING"
                   && !string.IsNullOrEmpty(r.ReferenceNumber)
                select r
            ).ToListAsync(ct);

            _logger.LogInformation(
                "ErpGatewaySalesMatchWorker: tenant {TenantId} — {ErpCount} ERP records, {GwCount} GATEWAY records",
                tenantId, erpRecords.Count, gatewayRecords.Count);

            // 3. Build GATEWAY lookup by ReferenceNumber (Order ID).
            var gatewayByRef = gatewayRecords
                .GroupBy(r => r.ReferenceNumber!.Trim().ToUpperInvariant())
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            var autoMatched = 0;
            var exceptions = 0;
            var noMatch = 0;
            var now = DateTime.UtcNow;

            foreach (var erpRecord in erpRecords)
            {
                var refKey = erpRecord.ReferenceNumber!.Trim().ToUpperInvariant();

                if (!gatewayByRef.TryGetValue(refKey, out var gatewayCandidates))
                {
                    // No Gateway charge for this ERP sale — revenue not collected!
                    tenantDb.ReconciliationEvents.Add(new ReconciliationEvent
                    {
                        ReconciliationEventId = Guid.NewGuid(),
                        ImportedNormalizedRecordId = erpRecord.ImportedNormalizedRecordId,
                        EventType = "MatchNotFound",
                        MatchLevel = "Level3",
                        Details = $"ERP record ref={erpRecord.ReferenceNumber} (amount={erpRecord.NetAmount}) " +
                                  $"has no corresponding Payment Gateway charge. Revenue may not have been collected.",
                        CreatedAt = now,
                    });
                    noMatch++;
                    continue;
                }

                // Find GATEWAY candidate with matching amount.
                var gatewayRecord = gatewayCandidates.FirstOrDefault(g =>
                    Math.Abs(g.NetAmount - erpRecord.NetAmount) < AmountTolerance);

                if (gatewayRecord == null)
                {
                    // Reference matches but amount differs — possible processing fee, refund, or partial.
                    var best = gatewayCandidates[0];
                    tenantDb.ReconciliationEvents.Add(new ReconciliationEvent
                    {
                        ReconciliationEventId = Guid.NewGuid(),
                        ImportedNormalizedRecordId = erpRecord.ImportedNormalizedRecordId,
                        EventType = "Variance",
                        MatchLevel = "Level3",
                        Details = $"Ref={erpRecord.ReferenceNumber}: ERP amount={erpRecord.NetAmount}, " +
                                  $"Gateway amount={best.NetAmount}, " +
                                  $"delta={Math.Abs(best.NetAmount - erpRecord.NetAmount):F2}. " +
                                  $"Possible processing fee deduction or partial refund.",
                        CreatedAt = now,
                    });
                    erpRecord.MatchStatus = "EXCEPTION";
                    exceptions++;
                    continue;
                }

                // Exact match — create Level3 group.
                var settlementKey = $"ERP_GW|{refKey}";
                var matchGroup = new ReconciliationMatchGroup
                {
                    ReconciliationMatchGroupId = Guid.NewGuid(),
                    MatchLevel = "Level3",
                    SettlementKey = settlementKey,
                    IsConfirmed = true,
                    ConfirmedAt = now,
                    MatchedAmount = erpRecord.NetAmount,
                    Variance = 0m,
                    Status = "Confirmed",
                    CreatedAt = now,
                };
                tenantDb.ReconciliationMatchGroups.Add(matchGroup);

                tenantDb.ReconciliationMatchedRecords.Add(new ReconciliationMatchedRecord
                {
                    ReconciliationMatchedRecordId = Guid.NewGuid(),
                    ReconciliationMatchGroupId = matchGroup.ReconciliationMatchGroupId,
                    ImportedNormalizedRecordId = erpRecord.ImportedNormalizedRecordId,
                    SourceType = "ERP",
                    LinkedAt = now,
                });
                tenantDb.ReconciliationMatchedRecords.Add(new ReconciliationMatchedRecord
                {
                    ReconciliationMatchedRecordId = Guid.NewGuid(),
                    ReconciliationMatchGroupId = matchGroup.ReconciliationMatchGroupId,
                    ImportedNormalizedRecordId = gatewayRecord.ImportedNormalizedRecordId,
                    SourceType = "GATEWAY",
                    LinkedAt = now,
                });

                // Mark GATEWAY record — still allows Level4/Level6 to re-use it for expense/settlement matching.
                gatewayRecord.MatchStatus = "LEVEL3_MATCHED";
                gatewayRecord.SettlementKey = settlementKey;
                // ERP side records the Sales Match outcome so the sale shows as verified downstream.
                erpRecord.MatchStatus = "SALES_VERIFIED";
                erpRecord.SettlementKey = settlementKey;

                _logger.LogInformation(
                    "Level3 AUTO-MATCHED: ERP {ErpId} ↔ GATEWAY {GwId} (ref={Ref})",
                    erpRecord.ImportedNormalizedRecordId, gatewayRecord.ImportedNormalizedRecordId, refKey);

                autoMatched++;
            }

            await tenantDb.SaveChangesAsync(ct);

            _logger.LogInformation(
                "ErpGatewaySalesMatchWorker done — matched={M}, exceptions={E}, noMatch={N}",
                autoMatched, exceptions, noMatch);

            return new MatchingRunResult("Level3", erpRecords.Count, autoMatched, exceptions, noMatch);
        }
    }
}
