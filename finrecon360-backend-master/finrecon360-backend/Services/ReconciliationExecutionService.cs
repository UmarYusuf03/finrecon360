using System.Text.Json;
using finrecon360_backend.Data;
using finrecon360_backend.Models;
using Microsoft.EntityFrameworkCore;

namespace finrecon360_backend.Services
{
    public record ReconciliationExecutionResult(
        string SourceType,
        int Level3VerifiedCount,
        int Level3ExceptionCount,
        int Level4MatchedCount,
        int Level4ExceptionCount,
        int WaitingForSettlementCount,
        decimal FeeAdjustmentTotal,
        string Summary);

    public interface IReconciliationExecutionService
    {
        Task<ReconciliationExecutionResult> ExecuteOnCommitAsync(
            TenantDbContext tenantDb,
            ImportBatch batch,
            IReadOnlyList<ImportedNormalizedRecord> committedRecords,
            CancellationToken ct = default);
    }

    public class ReconciliationExecutionService : IReconciliationExecutionService
    {
        private const decimal Tolerance = 0.01m;
        private static readonly TimeSpan PosAutoMatchWindow = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan PosReviewWindow = TimeSpan.FromMinutes(15);

        public async Task<ReconciliationExecutionResult> ExecuteOnCommitAsync(
            TenantDbContext tenantDb,
            ImportBatch batch,
            IReadOnlyList<ImportedNormalizedRecord> committedRecords,
            CancellationToken ct = default)
        {
            var sourceType = NormalizeSourceType(batch.SourceType);

            return sourceType switch
            {
                "ERP" => await ExecuteErpLevel3Async(tenantDb, batch.ImportBatchId, committedRecords, ct),
                "GATEWAY" => await ExecuteGatewayLevel3AndLevel4Async(tenantDb, batch.ImportBatchId, committedRecords, ct),
                "BANK" => await ExecuteBankLevel4Async(tenantDb, batch.ImportBatchId, committedRecords, ct),
                "POS" => await ExecutePosLevel1Async(tenantDb, batch.ImportBatchId, committedRecords, ct),
                _ => BuildResult(sourceType, 0, 0, 0, 0, 0, 0m, "No stage execution rule for source type.")
            };
        }

        /// <summary>
        /// WHY: POS Stage 1 — Operational Match (Internal Verification).
        /// Purpose: Verify that each POS End-of-Day (EOD) record matches a corresponding
        /// staff manual input entry in the ERP / sales ledger.
        /// Match key: ReferenceNumber + Amount + TransactionDate within a small time window.
        /// Reference and amount must agree; time is scored because POS/staff timestamps can drift
        /// by seconds or a few minutes.
        /// Outcome on full match: INTERNAL_VERIFIED match group.
        /// Outcome on near match: ManualReview or Variance — RequiresReview.
        /// Outcome on no match at all: MatchNotFound — Pending.
        /// </summary>
        private static async Task<ReconciliationExecutionResult> ExecutePosLevel1Async(
            TenantDbContext tenantDb,
            Guid importBatchId,
            IReadOnlyList<ImportedNormalizedRecord> posRecords,
            CancellationToken ct)
        {
            // Load all committed ERP records — these are the staff manual input / sales ledger entries
            // that POS EOD records should correspond to.
            var erpRecords = await QueryCommittedBySourceType(tenantDb, "ERP", ct);

            // Build a lookup: ReferenceNumber → list of ERP candidates (case-insensitive)
            var erpByReference = erpRecords
                .Where(x => !string.IsNullOrWhiteSpace(x.ReferenceNumber))
                .Select(x => new
                {
                    Record = x,
                    NormalizedReference = NormalizeReferenceNumber(x.ReferenceNumber)
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.NormalizedReference))
                .GroupBy(x => x.NormalizedReference!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Record).ToList(), StringComparer.OrdinalIgnoreCase);

            var verified = 0;
            var exceptions = 0;

            foreach (var pos in posRecords)
            {
                var posAmount = ResolveComparableAmount(pos);
                var posReference = NormalizeReferenceNumber(pos.ReferenceNumber);

                // Guard: POS record must have a reference number and a comparable amount
                if (string.IsNullOrWhiteSpace(posReference) || !posAmount.HasValue)
                {
                    var noDataEvent = new ReconciliationEvent
                    {
                        ReconciliationEventId = Guid.NewGuid(),
                        ImportBatchId = importBatchId,
                        ImportedNormalizedRecordId = pos.ImportedNormalizedRecordId,
                        EventType = "MatchNotFound",
                        Stage = "Level1",
                        SourceType = "POS",
                        Status = "Pending",
                        DetailJson = JsonSerializer.Serialize(new
                        {
                            reason = "Missing ReferenceNumber or ComparableAmount on POS record"
                        })
                    };
                    tenantDb.ReconciliationEvents.Add(noDataEvent);
                    exceptions++;
                    continue;
                }

                // Guard: No ERP candidate with this reference number
                if (!erpByReference.TryGetValue(posReference!, out var candidates))
                {
                    var notFoundEvent = new ReconciliationEvent
                    {
                        ReconciliationEventId = Guid.NewGuid(),
                        ImportBatchId = importBatchId,
                        ImportedNormalizedRecordId = pos.ImportedNormalizedRecordId,
                        EventType = "MatchNotFound",
                        Stage = "Level1",
                        SourceType = "POS",
                        Status = "Pending",
                        DetailJson = JsonSerializer.Serialize(new
                        {
                            referenceNumber = posReference,
                            reason = "No ERP staff-input record found with this reference"
                        })
                    };
                    tenantDb.ReconciliationEvents.Add(notFoundEvent);
                    exceptions++;
                    continue;
                }

                var scoredCandidates = candidates
                    .Select(candidate => ScorePosCandidate(pos, candidate, posAmount.Value, candidate.CreatedAt))
                    .Where(candidate => candidate.Score > 0)
                    .OrderByDescending(candidate => candidate.Score)
                    .ThenBy(candidate => candidate.TimeDelta)
                    .ThenBy(candidate => candidate.Record.CreatedAt)
                    .ToList();

                if (scoredCandidates.Count == 0)
                {
                    // Reference matched but amount diverged on every candidate.
                    var varianceEvent = new ReconciliationEvent
                    {
                        ReconciliationEventId = Guid.NewGuid(),
                        ImportBatchId = importBatchId,
                        ImportedNormalizedRecordId = pos.ImportedNormalizedRecordId,
                        EventType = "Variance",
                        Stage = "Level1",
                        SourceType = "POS",
                        Status = "RequiresReview",
                        DetailJson = JsonSerializer.Serialize(new
                        {
                            referenceNumber = posReference,
                            posAmount = posAmount.Value,
                            erpCandidates = candidates.Count,
                            reason = "Amount mismatch on all ERP candidates"
                        })
                    };
                    tenantDb.ReconciliationEvents.Add(varianceEvent);
                    exceptions++;
                    continue;
                }

                if (scoredCandidates.Count == 1)
                {
                    scoredCandidates[0] = scoredCandidates[0] with { Score = scoredCandidates[0].Score + 10 };
                }

                var bestCandidate = scoredCandidates[0];
                var isAmbiguous = scoredCandidates.Count > 1
                    && scoredCandidates[1].Score == bestCandidate.Score
                    && scoredCandidates[1].TimeDelta == bestCandidate.TimeDelta;

                if (!isAmbiguous
                    && bestCandidate.TimeDelta <= PosAutoMatchWindow
                    && bestCandidate.Score >= 85)
                {
                    var matchGroupId = Guid.NewGuid();
                    var primaryEventId = Guid.NewGuid();
                    var settlementKey = ResolveSettlementKey(pos) ?? pos.ReferenceNumber;

                    var matchGroup = new ReconciliationMatchGroup
                    {
                        ReconciliationMatchGroupId = matchGroupId,
                        ImportBatchId = importBatchId,
                        MatchLevel = "Level1",
                        SettlementKey = settlementKey,
                        PrimaryEventId = primaryEventId,
                        MatchMetadataJson = JsonSerializer.Serialize(new
                        {
                            referenceNumber = posReference,
                            posAmount = posAmount.Value,
                            erpAmount = bestCandidate.Amount,
                            posTransactionDate = pos.TransactionDate,
                            erpTransactionDate = bestCandidate.Record.TransactionDate,
                            timeDeltaSeconds = (int)bestCandidate.TimeDelta.TotalSeconds,
                            matchScore = bestCandidate.Score,
                            matchWindowMinutes = PosAutoMatchWindow.TotalMinutes
                        })
                    };
                    tenantDb.ReconciliationMatchGroups.Add(matchGroup);

                    var matchEvent = new ReconciliationEvent
                    {
                        ReconciliationEventId = primaryEventId,
                        ImportBatchId = importBatchId,
                        ImportedNormalizedRecordId = pos.ImportedNormalizedRecordId,
                        EventType = "MatchFound",
                        Stage = "Level1",
                        SourceType = "POS",
                        Status = "Completed",
                        DetailJson = JsonSerializer.Serialize(new
                        {
                            matchedErpRecordId = bestCandidate.Record.ImportedNormalizedRecordId,
                            referenceNumber = posReference,
                            timeDeltaSeconds = (int)bestCandidate.TimeDelta.TotalSeconds,
                            matchScore = bestCandidate.Score
                        })
                    };
                    tenantDb.ReconciliationEvents.Add(matchEvent);

                    tenantDb.ReconciliationMatchedRecords.Add(new ReconciliationMatchedRecord
                    {
                        ReconciliationMatchedRecordId = Guid.NewGuid(),
                        ReconciliationMatchGroupId = matchGroupId,
                        ImportedNormalizedRecordId = pos.ImportedNormalizedRecordId,
                        SourceType = "POS",
                        MatchAmount = posAmount.Value
                    });

                    tenantDb.ReconciliationMatchedRecords.Add(new ReconciliationMatchedRecord
                    {
                        ReconciliationMatchedRecordId = Guid.NewGuid(),
                        ReconciliationMatchGroupId = matchGroupId,
                        ImportedNormalizedRecordId = bestCandidate.Record.ImportedNormalizedRecordId,
                        SourceType = "ERP",
                        MatchAmount = bestCandidate.Amount
                    });

                    pos.MatchStatus = "MATCHED";

                    var trackedErpRecord = tenantDb.ChangeTracker.Entries<ImportedNormalizedRecord>()
                        .Select(entry => entry.Entity)
                        .FirstOrDefault(record => record.ImportedNormalizedRecordId == bestCandidate.Record.ImportedNormalizedRecordId);

                    if (trackedErpRecord is not null)
                    {
                        trackedErpRecord.MatchStatus = "MATCHED";
                    }
                    else
                    {
                        tenantDb.Attach(bestCandidate.Record);
                        bestCandidate.Record.MatchStatus = "MATCHED";
                        tenantDb.Entry(bestCandidate.Record).Property(x => x.MatchStatus).IsModified = true;
                    }
                    verified++;
                    continue;
                }

                if (!isAmbiguous
                    && bestCandidate.TimeDelta <= PosReviewWindow
                    && bestCandidate.Score >= 70)
                {
                    var reviewEvent = new ReconciliationEvent
                    {
                        ReconciliationEventId = Guid.NewGuid(),
                        ImportBatchId = importBatchId,
                        ImportedNormalizedRecordId = pos.ImportedNormalizedRecordId,
                        EventType = "ManualReview",
                        Stage = "Level1",
                        SourceType = "POS",
                        Status = "RequiresReview",
                        DetailJson = JsonSerializer.Serialize(new
                        {
                            referenceNumber = posReference,
                            posAmount = posAmount.Value,
                            matchedErpRecordId = bestCandidate.Record.ImportedNormalizedRecordId,
                            erpAmount = bestCandidate.Amount,
                            timeDeltaSeconds = (int)bestCandidate.TimeDelta.TotalSeconds,
                            matchScore = bestCandidate.Score,
                            reason = "Time window needs human confirmation"
                        })
                    };
                    tenantDb.ReconciliationEvents.Add(reviewEvent);
                    exceptions++;
                    continue;
                }

                var detailReason = isAmbiguous
                    ? "Multiple candidates share the same score and time delta"
                    : bestCandidate.TimeDelta > PosReviewWindow
                        ? "Time difference exceeds review window"
                        : "Match score below auto-match threshold";

                var varianceOrNoMatchEvent = new ReconciliationEvent
                {
                    ReconciliationEventId = Guid.NewGuid(),
                    ImportBatchId = importBatchId,
                    ImportedNormalizedRecordId = pos.ImportedNormalizedRecordId,
                    EventType = isAmbiguous ? "ManualReview" : "Variance",
                    Stage = "Level1",
                    SourceType = "POS",
                    Status = "RequiresReview",
                    DetailJson = JsonSerializer.Serialize(new
                    {
                        referenceNumber = posReference,
                        posAmount = posAmount.Value,
                        matchedErpRecordId = bestCandidate.Record.ImportedNormalizedRecordId,
                        erpAmount = bestCandidate.Amount,
                        timeDeltaSeconds = (int)bestCandidate.TimeDelta.TotalSeconds,
                        matchScore = bestCandidate.Score,
                        reason = detailReason
                    })
                };
                tenantDb.ReconciliationEvents.Add(varianceOrNoMatchEvent);
                exceptions++;
            }

            await tenantDb.SaveChangesAsync(ct);

            return BuildResult(
                "POS",
                verified,
                exceptions,
                0,
                0,
                0,
                0m,
                $"Level1 POS Stage-1 Operational Match completed: internalVerified={verified};exceptions={exceptions};matchKey=ReferenceNumber+Amount+TimeWindow");
        }

        private static async Task<ReconciliationExecutionResult> ExecuteErpLevel3Async(
            TenantDbContext tenantDb,
            Guid importBatchId,
            IReadOnlyList<ImportedNormalizedRecord> erpRecords,
            CancellationToken ct)
        {
            var gatewayRecords = await QueryCommittedBySourceType(tenantDb, "GATEWAY", ct);
            var gatewayByReference = gatewayRecords
                .Where(x => !string.IsNullOrWhiteSpace(x.ReferenceNumber))
                .GroupBy(x => x.ReferenceNumber!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            var verified = 0;
            var exceptions = 0;

            foreach (var erp in erpRecords)
            {
                var erpAmount = ResolveComparableAmount(erp);
                
                if (string.IsNullOrWhiteSpace(erp.ReferenceNumber) || !erpAmount.HasValue)
                {
                    // Create MatchNotFound event for missing reference or amount
                    var noRefEvent = new ReconciliationEvent
                    {
                        ReconciliationEventId = Guid.NewGuid(),
                        ImportBatchId = importBatchId,
                        ImportedNormalizedRecordId = erp.ImportedNormalizedRecordId,
                        EventType = "MatchNotFound",
                        Stage = "Level3",
                        SourceType = "ERP",
                        Status = "Pending",
                        DetailJson = $"{{\"reason\":\"Missing ReferenceNumber or ComparableAmount\"}}"
                    };
                    tenantDb.ReconciliationEvents.Add(noRefEvent);
                    exceptions++;
                    continue;
                }

                if (!gatewayByReference.TryGetValue(erp.ReferenceNumber!, out var candidates))
                {
                    // Create MatchNotFound event
                    var notFoundEvent = new ReconciliationEvent
                    {
                        ReconciliationEventId = Guid.NewGuid(),
                        ImportBatchId = importBatchId,
                        ImportedNormalizedRecordId = erp.ImportedNormalizedRecordId,
                        EventType = "MatchNotFound",
                        Stage = "Level3",
                        SourceType = "ERP",
                        Status = "Pending",
                        DetailJson = $"{{\"referenceNumber\":\"{erp.ReferenceNumber}\"}}"
                    };
                    tenantDb.ReconciliationEvents.Add(notFoundEvent);
                    exceptions++;
                    continue;
                }

                var matchedGateway = candidates.FirstOrDefault(g =>
                {
                    var gatewayGross = g.GrossAmount ?? g.NetAmount;
                    return Math.Abs(gatewayGross - erpAmount.Value) <= Tolerance;
                });

                if (matchedGateway != null)
                {
                    // Create MatchFound event and match group
                    var matchGroupId = Guid.NewGuid();
                    var eventId = Guid.NewGuid();
                    
                    var matchGroup = new ReconciliationMatchGroup
                    {
                        ReconciliationMatchGroupId = matchGroupId,
                        ImportBatchId = importBatchId,
                        MatchLevel = "Level3",
                        SettlementKey = ResolveSettlementKey(erp),
                        PrimaryEventId = eventId,
                        MatchMetadataJson = $"{{\"erpAmount\":{erpAmount},\"gatewayGross\":{matchedGateway.GrossAmount ?? matchedGateway.NetAmount}}}"
                    };
                    tenantDb.ReconciliationMatchGroups.Add(matchGroup);

                    var matchEvent = new ReconciliationEvent
                    {
                        ReconciliationEventId = eventId,
                        ImportBatchId = importBatchId,
                        ImportedNormalizedRecordId = erp.ImportedNormalizedRecordId,
                        EventType = "MatchFound",
                        Stage = "Level3",
                        SourceType = "ERP",
                        Status = "Completed",
                        DetailJson = $"{{\"matchedGatewayRecordId\":\"{matchedGateway.ImportedNormalizedRecordId}\"}}"
                    };
                    tenantDb.ReconciliationEvents.Add(matchEvent);

                    // Add matched records
                    tenantDb.ReconciliationMatchedRecords.Add(new ReconciliationMatchedRecord
                    {
                        ReconciliationMatchedRecordId = Guid.NewGuid(),
                        ReconciliationMatchGroupId = matchGroupId,
                        ImportedNormalizedRecordId = erp.ImportedNormalizedRecordId,
                        SourceType = "ERP",
                        MatchAmount = erpAmount.Value
                    });

                    tenantDb.ReconciliationMatchedRecords.Add(new ReconciliationMatchedRecord
                    {
                        ReconciliationMatchedRecordId = Guid.NewGuid(),
                        ReconciliationMatchGroupId = matchGroupId,
                        ImportedNormalizedRecordId = matchedGateway.ImportedNormalizedRecordId,
                        SourceType = "GATEWAY",
                        MatchAmount = matchedGateway.GrossAmount ?? matchedGateway.NetAmount
                    });

                    verified++;
                }
                else
                {
                    // Create Variance event
                    var varianceEvent = new ReconciliationEvent
                    {
                        ReconciliationEventId = Guid.NewGuid(),
                        ImportBatchId = importBatchId,
                        ImportedNormalizedRecordId = erp.ImportedNormalizedRecordId,
                        EventType = "Variance",
                        Stage = "Level3",
                        SourceType = "ERP",
                        Status = "RequiresReview",
                        DetailJson = $"{{\"erpAmount\":{erpAmount},\"candidates\":{candidates.Count}}}"
                    };
                    tenantDb.ReconciliationEvents.Add(varianceEvent);
                    exceptions++;
                }
            }

            await tenantDb.SaveChangesAsync(ct);

            return BuildResult(
                "ERP",
                verified,
                exceptions,
                0,
                0,
                0,
                0m,
                $"Level3 ERP->Gateway gross check completed: verified={verified};exceptions={exceptions}");
        }

        private static async Task<ReconciliationExecutionResult> ExecuteGatewayLevel3AndLevel4Async(
            TenantDbContext tenantDb,
            Guid importBatchId,
            IReadOnlyList<ImportedNormalizedRecord> gatewayRecords,
            CancellationToken ct)
        {
            var erpRecords = await QueryCommittedBySourceType(tenantDb, "ERP", ct);
            var erpByReference = erpRecords
                .Where(x => !string.IsNullOrWhiteSpace(x.ReferenceNumber))
                .GroupBy(x => x.ReferenceNumber!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            var level3Verified = 0;
            var level3Exceptions = 0;
            var waitingForSettlement = 0;

            foreach (var gateway in gatewayRecords)
            {
                var settlementKey = ResolveSettlementKey(gateway);
                
                if (string.IsNullOrWhiteSpace(settlementKey))
                {
                    // Create ManualReview event for missing settlement key
                    var noKeyEvent = new ReconciliationEvent
                    {
                        ReconciliationEventId = Guid.NewGuid(),
                        ImportBatchId = importBatchId,
                        ImportedNormalizedRecordId = gateway.ImportedNormalizedRecordId,
                        EventType = "ManualReview",
                        Stage = "Level3",
                        SourceType = "GATEWAY",
                        Status = "RequiresReview",
                        DetailJson = "{\"reason\":\"Missing settlement key (AccountCode and ReferenceNumber)\"}"
                    };
                    tenantDb.ReconciliationEvents.Add(noKeyEvent);
                    waitingForSettlement++;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(gateway.ReferenceNumber))
                {
                    var noRefEvent = new ReconciliationEvent
                    {
                        ReconciliationEventId = Guid.NewGuid(),
                        ImportBatchId = importBatchId,
                        ImportedNormalizedRecordId = gateway.ImportedNormalizedRecordId,
                        EventType = "MatchNotFound",
                        Stage = "Level3",
                        SourceType = "GATEWAY",
                        Status = "Pending",
                        DetailJson = "{\"reason\":\"Missing ReferenceNumber\"}"
                    };
                    tenantDb.ReconciliationEvents.Add(noRefEvent);
                    level3Exceptions++;
                    continue;
                }

                if (!erpByReference.TryGetValue(gateway.ReferenceNumber!, out var candidates))
                {
                    var notFoundEvent = new ReconciliationEvent
                    {
                        ReconciliationEventId = Guid.NewGuid(),
                        ImportBatchId = importBatchId,
                        ImportedNormalizedRecordId = gateway.ImportedNormalizedRecordId,
                        EventType = "MatchNotFound",
                        Stage = "Level3",
                        SourceType = "GATEWAY",
                        Status = "Pending",
                        DetailJson = $"{{\"referenceNumber\":\"{gateway.ReferenceNumber}\"}}"
                    };
                    tenantDb.ReconciliationEvents.Add(notFoundEvent);
                    level3Exceptions++;
                    continue;
                }

                var gatewayGross = gateway.GrossAmount ?? gateway.NetAmount;
                var matchedErp = candidates.FirstOrDefault(e =>
                {
                    var erpAmount = ResolveComparableAmount(e);
                    return erpAmount.HasValue && Math.Abs(erpAmount.Value - gatewayGross) <= Tolerance;
                });

                if (matchedErp != null)
                {
                    // Create MatchFound event and match group for Level 3
                    var matchGroupId = Guid.NewGuid();
                    var eventId = Guid.NewGuid();

                    var matchGroup = new ReconciliationMatchGroup
                    {
                        ReconciliationMatchGroupId = matchGroupId,
                        ImportBatchId = importBatchId,
                        MatchLevel = "Level3",
                        SettlementKey = settlementKey,
                        PrimaryEventId = eventId,
                        MatchMetadataJson = $"{{\"gatewayGross\":{gatewayGross},\"erpAmount\":{ResolveComparableAmount(matchedErp)}}}"
                    };
                    tenantDb.ReconciliationMatchGroups.Add(matchGroup);

                    var matchEvent = new ReconciliationEvent
                    {
                        ReconciliationEventId = eventId,
                        ImportBatchId = importBatchId,
                        ImportedNormalizedRecordId = gateway.ImportedNormalizedRecordId,
                        EventType = "MatchFound",
                        Stage = "Level3",
                        SourceType = "GATEWAY",
                        Status = "Completed",
                        DetailJson = $"{{\"matchedErpRecordId\":\"{matchedErp.ImportedNormalizedRecordId}\"}}"
                    };
                    tenantDb.ReconciliationEvents.Add(matchEvent);

                    // Add matched records
                    tenantDb.ReconciliationMatchedRecords.Add(new ReconciliationMatchedRecord
                    {
                        ReconciliationMatchedRecordId = Guid.NewGuid(),
                        ReconciliationMatchGroupId = matchGroupId,
                        ImportedNormalizedRecordId = gateway.ImportedNormalizedRecordId,
                        SourceType = "GATEWAY",
                        MatchAmount = gatewayGross
                    });

                    tenantDb.ReconciliationMatchedRecords.Add(new ReconciliationMatchedRecord
                    {
                        ReconciliationMatchedRecordId = Guid.NewGuid(),
                        ReconciliationMatchGroupId = matchGroupId,
                        ImportedNormalizedRecordId = matchedErp.ImportedNormalizedRecordId,
                        SourceType = "ERP",
                        MatchAmount = ResolveComparableAmount(matchedErp) ?? 0m
                    });

                    level3Verified++;
                }
                else
                {
                    var varianceEvent = new ReconciliationEvent
                    {
                        ReconciliationEventId = Guid.NewGuid(),
                        ImportBatchId = importBatchId,
                        ImportedNormalizedRecordId = gateway.ImportedNormalizedRecordId,
                        EventType = "Variance",
                        Stage = "Level3",
                        SourceType = "GATEWAY",
                        Status = "RequiresReview",
                        DetailJson = $"{{\"gatewayGross\":{gatewayGross},\"candidates\":{candidates.Count}}}"
                    };
                    tenantDb.ReconciliationEvents.Add(varianceEvent);
                    level3Exceptions++;
                }
            }

            await tenantDb.SaveChangesAsync(ct);

            var summary = $"Gateway execution completed: level3Verified={level3Verified};level3Exceptions={level3Exceptions};waitingForSettlement={waitingForSettlement};level4 deferred until bank import.";
            return BuildResult("GATEWAY", level3Verified, level3Exceptions, 0, 0, waitingForSettlement, 0m, summary);
        }

        private static async Task<ReconciliationExecutionResult> ExecuteBankLevel4Async(
            TenantDbContext tenantDb,
            Guid importBatchId,
            IReadOnlyList<ImportedNormalizedRecord> bankRecords,
            CancellationToken ct)
        {
            var gatewayRecords = await QueryCommittedBySourceType(tenantDb, "GATEWAY", ct);

            var gatewayGroups = gatewayRecords
                .Select(x => new
                {
                    Record = x,
                    SettlementKey = ResolveSettlementKey(x),
                    NetAmount = x.NetAmount,
                    ProcessingFee = x.ProcessingFee ?? 0m
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.SettlementKey))
                .GroupBy(x => x.SettlementKey!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => new
                    {
                        Records = g.Select(v => v.Record).ToList(),
                        NetTotal = g.Sum(v => v.NetAmount),
                        FeeTotal = g.Sum(v => v.ProcessingFee)
                    },
                    StringComparer.OrdinalIgnoreCase);

            var bankBySettlement = bankRecords
                .Select(x => new
                {
                    Record = x,
                    SettlementKey = ResolveSettlementKey(x),
                    DepositAmount = x.NetAmount
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.SettlementKey))
                .GroupBy(x => x.SettlementKey!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => new
                    {
                        Records = g.Select(v => v.Record).ToList(),
                        Total = g.Sum(v => v.DepositAmount)
                    },
                    StringComparer.OrdinalIgnoreCase);

            var matched = 0;
            var exceptions = 0;
            decimal feeAdjustment = 0m;

            foreach (var (settlementKey, aggregate) in gatewayGroups)
            {
                if (!bankBySettlement.TryGetValue(settlementKey, out var bankAggregate))
                {
                    // Create MatchNotFound event for each gateway record in this settlement
                    foreach (var gwRecord in aggregate.Records)
                    {
                        var notFoundEvent = new ReconciliationEvent
                        {
                            ReconciliationEventId = Guid.NewGuid(),
                            ImportBatchId = importBatchId,
                            ImportedNormalizedRecordId = gwRecord.ImportedNormalizedRecordId,
                            EventType = "MatchNotFound",
                            Stage = "Level4",
                            SourceType = "GATEWAY",
                            Status = "Pending",
                            DetailJson = $"{{\"settlementKey\":\"{settlementKey}\",\"gatewayNetTotal\":{aggregate.NetTotal}}}"
                        };
                        tenantDb.ReconciliationEvents.Add(notFoundEvent);
                    }
                    exceptions++;
                    continue;
                }

                if (Math.Abs(bankAggregate.Total - aggregate.NetTotal) <= Tolerance)
                {
                    // Create MatchFound event and match group for Level 4
                    var matchGroupId = Guid.NewGuid();
                    var eventId = Guid.NewGuid();

                    var matchGroup = new ReconciliationMatchGroup
                    {
                        ReconciliationMatchGroupId = matchGroupId,
                        ImportBatchId = importBatchId,
                        MatchLevel = "Level4",
                        SettlementKey = settlementKey,
                        PrimaryEventId = eventId,
                        MatchMetadataJson = $"{{\"gatewayNetTotal\":{aggregate.NetTotal},\"bankTotal\":{bankAggregate.Total},\"feeTotal\":{aggregate.FeeTotal}}}"
                    };
                    tenantDb.ReconciliationMatchGroups.Add(matchGroup);

                    var matchEvent = new ReconciliationEvent
                    {
                        ReconciliationEventId = eventId,
                        ImportBatchId = importBatchId,
                        ImportedNormalizedRecordId = aggregate.Records.First().ImportedNormalizedRecordId,
                        EventType = "MatchFound",
                        Stage = "Level4",
                        SourceType = "BANK",
                        Status = "Completed",
                        DetailJson = $"{{\"gatewayRecordsCount\":{aggregate.Records.Count},\"bankRecordsCount\":{bankAggregate.Records.Count}}}"
                    };
                    tenantDb.ReconciliationEvents.Add(matchEvent);

                    // Add matched records
                    foreach (var gwRecord in aggregate.Records)
                    {
                        tenantDb.ReconciliationMatchedRecords.Add(new ReconciliationMatchedRecord
                        {
                            ReconciliationMatchedRecordId = Guid.NewGuid(),
                            ReconciliationMatchGroupId = matchGroupId,
                            ImportedNormalizedRecordId = gwRecord.ImportedNormalizedRecordId,
                            SourceType = "GATEWAY",
                            MatchAmount = gwRecord.NetAmount
                        });
                    }

                    foreach (var bankRecord in bankAggregate.Records)
                    {
                        tenantDb.ReconciliationMatchedRecords.Add(new ReconciliationMatchedRecord
                        {
                            ReconciliationMatchedRecordId = Guid.NewGuid(),
                            ReconciliationMatchGroupId = matchGroupId,
                            ImportedNormalizedRecordId = bankRecord.ImportedNormalizedRecordId,
                            SourceType = "BANK",
                            MatchAmount = bankRecord.NetAmount
                        });
                    }

                    // Create ProcessingFeeAdjustment event
                    if (aggregate.FeeTotal > 0m)
                    {
                        var feeEvent = new ReconciliationEvent
                        {
                            ReconciliationEventId = Guid.NewGuid(),
                            ImportBatchId = importBatchId,
                            ImportedNormalizedRecordId = aggregate.Records.First().ImportedNormalizedRecordId,
                            EventType = "ProcessingFeeAdjustment",
                            Stage = "Level4",
                            SourceType = "BANK",
                            Status = "Completed",
                            DetailJson = $"{{\"totalFee\":{aggregate.FeeTotal},\"settlementKey\":\"{settlementKey}\"}}"
                        };
                        tenantDb.ReconciliationEvents.Add(feeEvent);
                    }

                    feeAdjustment += aggregate.FeeTotal;
                    matched++;
                }
                else
                {
                    // Create Variance event
                    var varianceEvent = new ReconciliationEvent
                    {
                        ReconciliationEventId = Guid.NewGuid(),
                        ImportBatchId = importBatchId,
                        ImportedNormalizedRecordId = aggregate.Records.First().ImportedNormalizedRecordId,
                        EventType = "Variance",
                        Stage = "Level4",
                        SourceType = "BANK",
                        Status = "RequiresReview",
                        DetailJson = $"{{\"settlementKey\":\"{settlementKey}\",\"gatewayNetTotal\":{aggregate.NetTotal},\"bankTotal\":{bankAggregate.Total},\"variance\":{Math.Abs(bankAggregate.Total - aggregate.NetTotal)}}}"
                    };
                    tenantDb.ReconciliationEvents.Add(varianceEvent);
                    exceptions++;
                }
            }

            await tenantDb.SaveChangesAsync(ct);

            var summary = $"Level4 Gateway->Bank net check completed: matched={matched};exceptions={exceptions};feeAdjustmentTotal={feeAdjustment:0.##};settlementKeyField=AccountCode|ReferenceNumber";

            return BuildResult("BANK", 0, 0, matched, exceptions, 0, feeAdjustment, summary);
        }

        private static async Task<List<ImportedNormalizedRecord>> QueryCommittedBySourceType(TenantDbContext tenantDb, string sourceType, CancellationToken ct)
        {
            var normalizedSource = NormalizeSourceType(sourceType);

            return await tenantDb.ImportedNormalizedRecords
                .AsNoTracking()
                .Where(x => x.ImportBatch != null
                    && x.ImportBatch.SourceType.ToUpper() == normalizedSource
                    && x.ImportBatch.Status == "COMMITTED")
                .ToListAsync(ct);
        }

        private static decimal? ResolveComparableAmount(ImportedNormalizedRecord record)
        {
            return record.GrossAmount ?? record.NetAmount;
        }

        private static PosMatchCandidate ScorePosCandidate(
            ImportedNormalizedRecord posRecord,
            ImportedNormalizedRecord candidate,
            decimal posAmount,
            DateTime candidateCreatedAt)
        {
            var candidateAmount = ResolveComparableAmount(candidate);
            if (!candidateAmount.HasValue || Math.Abs(candidateAmount.Value - posAmount) > Tolerance)
            {
                return new PosMatchCandidate(candidate, candidateAmount ?? 0m, TimeSpan.MaxValue, 0);
            }

            // Use candidateCreatedAt (server timestamp) instead of candidate.TransactionDate for ERP staff-entered records
            var timeDelta = GetAbsoluteTimeDelta(posRecord.TransactionDate, candidateCreatedAt);
            var score = 60 + GetPosTimeScore(timeDelta);

            return new PosMatchCandidate(candidate, candidateAmount.Value, timeDelta, score);
        }

        private static int GetPosTimeScore(TimeSpan timeDelta)
        {
            if (timeDelta <= TimeSpan.FromSeconds(30))
            {
                return 30;
            }

            if (timeDelta <= TimeSpan.FromMinutes(2))
            {
                return 25;
            }

            if (timeDelta <= TimeSpan.FromMinutes(5))
            {
                return 20;
            }

            if (timeDelta <= PosReviewWindow)
            {
                return 10;
            }

            return 0;
        }

        private static TimeSpan GetAbsoluteTimeDelta(DateTime left, DateTime right)
        {
            var leftUtc = NormalizeToUtc(left);
            var rightUtc = NormalizeToUtc(right);
            return TimeSpan.FromTicks(Math.Abs((leftUtc - rightUtc).Ticks));
        }

        private static DateTime NormalizeToUtc(DateTime value)
        {
            return value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value, DateTimeKind.Local).ToUniversalTime()
            };
        }

        private static string? NormalizeReferenceNumber(string? referenceNumber)
        {
            if (string.IsNullOrWhiteSpace(referenceNumber))
            {
                return null;
            }

            var trimmed = referenceNumber.Trim();
            return trimmed.Length == 0 ? null : trimmed.ToUpperInvariant();
        }

        private sealed record PosMatchCandidate(
            ImportedNormalizedRecord Record,
            decimal Amount,
            TimeSpan TimeDelta,
            int Score);

        private static string? ResolveSettlementKey(ImportedNormalizedRecord record)
        {
            if (!string.IsNullOrWhiteSpace(record.AccountCode))
            {
                return record.AccountCode.Trim();
            }

            if (!string.IsNullOrWhiteSpace(record.ReferenceNumber))
            {
                return record.ReferenceNumber.Trim();
            }

            return null;
        }

        private static ReconciliationExecutionResult BuildResult(
            string sourceType,
            int level3Verified,
            int level3Exceptions,
            int level4Matched,
            int level4Exceptions,
            int waitingForSettlement,
            decimal feeAdjustment,
            string summary)
        {
            return new ReconciliationExecutionResult(
                sourceType,
                level3Verified,
                level3Exceptions,
                level4Matched,
                level4Exceptions,
                waitingForSettlement,
                feeAdjustment,
                summary);
        }

        private static string NormalizeSourceType(string? sourceType)
        {
            return string.IsNullOrWhiteSpace(sourceType)
                ? string.Empty
                : sourceType.Trim().ToUpperInvariant();
        }
    }
}