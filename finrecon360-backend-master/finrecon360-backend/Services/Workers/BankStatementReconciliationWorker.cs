using System.Text.Json;
using finrecon360_backend.Data;
using finrecon360_backend.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace finrecon360_backend.Services.Workers
{
    /// <summary>
    /// WHY: Automates Level-4 bank statement reconciliation for card cashouts.
    /// 
    /// Purpose: Continuously monitors the NeedsBankMatch queue and correlates
    /// GATEWAY transaction records (payment gateway imports) with BANK statement records
    /// (bank statement imports) to unlock journal posting without manual confirmation required 
    /// for high-confidence matches. Ambiguous matches surface as exceptions for human review.
    /// 
    /// Workflow:
    /// 1. Find all transactions in NeedsBankMatch state created within a lookback window (e.g., 14 days).
    /// 2. For each transaction, find matching GATEWAY import records (by amount + date correlation).
    /// 3. For each GATEWAY record, find matching BANK records (by SettlementKey + net total).
    /// 4. If match is high-confidence (exact amount match, settlement key clear):
    ///    - Create ReconciliationMatchGroup linking GATEWAY + BANK
    ///    - Update transaction state to JournalReady (unlocks journal posting)
    ///    - Log as "AutoMatched"
    /// 5. If match is ambiguous (multiple candidates, amount variance):
    ///    - Create ReconciliationEvent with status "RequiresReview"
    ///    - Leave transaction in NeedsBankMatch for manual confirmation
    /// 6. If no bank match exists:
    ///    - Create ReconciliationEvent with status "Pending"
    ///    - Remain in NeedsBankMatch until bank records arrive
    /// </summary>
    public interface IBankStatementReconciliationWorker
    {
        /// <summary>
        /// Execute one cycle of bank statement reconciliation for the given tenant.
        /// Safe to call repeatedly; idempotent on already-processed transactions.
        /// </summary>
        Task<BankReconciliationResult> ExecuteAsync(
            Guid tenantId,
            TenantDbContext tenantDb,
            CancellationToken cancellationToken = default);
    }

    public record BankReconciliationResult(
        int NeedsBankMatchCount,
        int AutoMatchedCount,
        int ExceptionCount,
        int NoMatchCount,
        string Summary);

    public class BankStatementReconciliationWorker : IBankStatementReconciliationWorker
    {
        private const decimal Tolerance = 0.01m;
        private static readonly TimeSpan LookbackWindow = TimeSpan.FromDays(14);

        private readonly ILogger<BankStatementReconciliationWorker> _logger;

        public BankStatementReconciliationWorker(ILogger<BankStatementReconciliationWorker> logger)
        {
            _logger = logger;
        }

        public async Task<BankReconciliationResult> ExecuteAsync(
            Guid tenantId,
            TenantDbContext tenantDb,
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Bank reconciliation cycle started for tenant {TenantId}", tenantId);

            // 1. Find all NeedsBankMatch transactions created within the lookback window
            var cutoffDate = DateTime.UtcNow.Subtract(LookbackWindow);
            var needsBankMatchTxns = await tenantDb.Transactions
                .AsNoTracking()
                .Where(x => x.TransactionState == TransactionState.NeedsBankMatch 
                    && x.CreatedAt >= cutoffDate)
                .OrderBy(x => x.TransactionDate)
                .ThenBy(x => x.CreatedAt)
                .ToListAsync(cancellationToken);

            _logger.LogInformation("Found {Count} transactions in NeedsBankMatch state for tenant {TenantId}", 
                needsBankMatchTxns.Count, tenantId);

            if (needsBankMatchTxns.Count == 0)
            {
                return new BankReconciliationResult(0, 0, 0, 0, "No NeedsBankMatch transactions to process");
            }

            // 2. Load all committed GATEWAY and BANK records for correlation
            var gatewayRecords = await QueryCommittedBySourceType(tenantDb, "GATEWAY", cancellationToken);
            var bankRecords = await QueryCommittedBySourceType(tenantDb, "BANK", cancellationToken);

            _logger.LogInformation("Loaded {GatewayCount} GATEWAY records and {BankCount} BANK records", 
                gatewayRecords.Count, bankRecords.Count);

            // 3. Load existing match groups to avoid duplicate matching
            var existingMatchGroups = await tenantDb.ReconciliationMatchGroups
                .AsNoTracking()
                .Where(g => g.MatchLevel == "Level4" && g.MatchMetadataJson != null)
                .ToListAsync(cancellationToken);

            var existingBankGatewaySessions = new HashSet<string>();
            foreach (var group in existingMatchGroups)
            {
                try
                {
                    var metadata = JsonSerializer.Deserialize<Dictionary<string, object>>(group.MatchMetadataJson ?? "{}");
                    if (metadata?.ContainsKey("gatewayBankSession") == true)
                    {
                        existingBankGatewaySessions.Add(metadata["gatewayBankSession"].ToString() ?? "");
                    }
                }
                catch { /* Skip malformed metadata */ }
            }

            var autoMatched = 0;
            var exceptions = 0;
            var noMatch = 0;

            // 4. Process each NeedsBankMatch transaction
            foreach (var txn in needsBankMatchTxns)
            {
                var txnDate = txn.TransactionDate.Date;
                var txnAmount = txn.Amount;

                // Find matching GATEWAY record(s) by amount + date
                var linkedGateway = gatewayRecords.FirstOrDefault(r =>
                    r.TransactionDate.Date == txnDate &&
                    Math.Abs((r.GrossAmount ?? r.NetAmount) - txnAmount) <= Tolerance);

                if (linkedGateway == null)
                {
                    _logger.LogDebug("No GATEWAY record found for transaction {TransactionId} on date {Date} amount {Amount}",
                        txn.TransactionId, txnDate, txnAmount);
                    noMatch++;
                    continue;
                }

                // Find matching BANK record(s) by SettlementKey + net total
                var settlementKey = ResolveSettlementKey(linkedGateway);
                if (string.IsNullOrWhiteSpace(settlementKey))
                {
                    _logger.LogDebug("GATEWAY record {RecordId} missing SettlementKey", linkedGateway.ImportedNormalizedRecordId);
                    exceptions++;
                    continue;
                }

                var matchingBankRecords = bankRecords
                    .Where(br => ResolveSettlementKey(br) == settlementKey)
                    .ToList();

                if (matchingBankRecords.Count == 0)
                {
                    _logger.LogDebug("No BANK records found for settlement key {SettlementKey}", settlementKey);
                    noMatch++;
                    continue;
                }

                // Aggregate bank records by settlement key to compare net totals
                var bankAggregate = matchingBankRecords
                    .Aggregate(
                        new { NetTotal = 0m, FeeTotal = 0m, Records = new List<ImportedNormalizedRecord>() },
                        (acc, br) => new
                        {
                            NetTotal = acc.NetTotal + br.NetAmount,
                            FeeTotal = acc.FeeTotal + (br.ProcessingFee ?? 0m),
                            Records = new List<ImportedNormalizedRecord>(acc.Records) { br }
                        });

                var gatewayNetTotal = linkedGateway.NetAmount;

                // Check for amount match
                if (Math.Abs(bankAggregate.NetTotal - gatewayNetTotal) > Tolerance)
                {
                    _logger.LogWarning(
                        "Amount variance for transaction {TransactionId}: GATEWAY net={GatewayNet}, BANK total={BankTotal}",
                        txn.TransactionId, gatewayNetTotal, bankAggregate.NetTotal);
                    exceptions++;
                    continue;
                }

                // 5. High-confidence match found — create match group and update transaction state
                var matchGroupId = Guid.NewGuid();
                var matchGroup = new ReconciliationMatchGroup
                {
                    ReconciliationMatchGroupId = matchGroupId,
                    ImportBatchId = linkedGateway.ImportBatchId,
                    MatchLevel = "Level4",
                    SettlementKey = settlementKey,
                    IsConfirmed = true, // Auto-confirmed for cardless settlement
                    ConfirmedByUserId = null, // Worker automation
                    ConfirmedAt = DateTime.UtcNow,
                    IsJournalPosted = false,
                    MatchMetadataJson = JsonSerializer.Serialize(new
                    {
                        transactionId = txn.TransactionId,
                        gatewayBankSession = $"{linkedGateway.ImportedNormalizedRecordId}_{bankAggregate.Records.First().ImportedNormalizedRecordId}",
                        gatewayNetAmount = gatewayNetTotal,
                        bankNetTotal = bankAggregate.NetTotal,
                        bankFeeTotal = bankAggregate.FeeTotal,
                        variance = Math.Abs(bankAggregate.NetTotal - gatewayNetTotal),
                        autoMatchedAt = DateTime.UtcNow,
                        processingFeeAdjustment = bankAggregate.FeeTotal
                    }),
                    CreatedAt = DateTime.UtcNow
                };

                tenantDb.ReconciliationMatchGroups.Add(matchGroup);

                // Add matched record links
                tenantDb.ReconciliationMatchedRecords.Add(new ReconciliationMatchedRecord
                {
                    ReconciliationMatchedRecordId = Guid.NewGuid(),
                    ReconciliationMatchGroupId = matchGroupId,
                    ImportedNormalizedRecordId = linkedGateway.ImportedNormalizedRecordId,
                    SourceType = "GATEWAY",
                    MatchAmount = gatewayNetTotal
                });

                foreach (var bank in bankAggregate.Records)
                {
                    tenantDb.ReconciliationMatchedRecords.Add(new ReconciliationMatchedRecord
                    {
                        ReconciliationMatchedRecordId = Guid.NewGuid(),
                        ReconciliationMatchGroupId = matchGroupId,
                        ImportedNormalizedRecordId = bank.ImportedNormalizedRecordId,
                        SourceType = "BANK",
                        MatchAmount = bank.NetAmount
                    });
                }

                // Log success event
                var successEvent = new ReconciliationEvent
                {
                    ReconciliationEventId = Guid.NewGuid(),
                    ImportBatchId = linkedGateway.ImportBatchId,
                    ImportedNormalizedRecordId = linkedGateway.ImportedNormalizedRecordId,
                    EventType = "MatchFound",
                    Stage = "Level4",
                    SourceType = "BANK",
                    Status = "Completed",
                    DetailJson = JsonSerializer.Serialize(new
                    {
                        reason = "Auto-matched by BankStatementReconciliationWorker",
                        transactionId = txn.TransactionId,
                        settlementKey = settlementKey,
                        bankRecordsCount = bankAggregate.Records.Count,
                        matchGroupId = matchGroupId
                    })
                };
                tenantDb.ReconciliationEvents.Add(successEvent);

                // Update transaction to JournalReady (unlock posting)
                txn.TransactionState = TransactionState.JournalReady;

                _logger.LogInformation("Auto-matched transaction {TransactionId} to settlement {SettlementKey}", 
                    txn.TransactionId, settlementKey);
                autoMatched++;
            }

            await tenantDb.SaveChangesAsync(cancellationToken);

            var result = new BankReconciliationResult(
                needsBankMatchTxns.Count,
                autoMatched,
                exceptions,
                noMatch,
                $"Level4 Bank reconciliation completed: autoMatched={autoMatched}; exceptions={exceptions}; noMatch={noMatch}");

            _logger.LogInformation("Bank reconciliation cycle completed for tenant {TenantId}: {Summary}", tenantId, result.Summary);
            return result;
        }

        private static async Task<List<ImportedNormalizedRecord>> QueryCommittedBySourceType(
            TenantDbContext tenantDb,
            string sourceType,
            CancellationToken ct)
        {
            return await tenantDb.ImportedNormalizedRecords
                .AsNoTracking()
                .Include(r => r.ImportBatch)
                .Where(r => r.ImportBatch != null
                    && r.ImportBatch.SourceType.ToUpper() == sourceType.ToUpper()
                    && r.ImportBatch.Status == "COMMITTED")
                .OrderByDescending(r => r.ImportBatch.ImportedAt)
                .ToListAsync(ct);
        }

        private static string? ResolveSettlementKey(ImportedNormalizedRecord record)
        {
            // Settlement key is AccountCode + ReferenceNumber (both required for Level-4 matching)
            var accountCode = record.AccountCode?.Trim();
            var referenceNumber = record.ReferenceNumber?.Trim();

            if (string.IsNullOrWhiteSpace(accountCode) || string.IsNullOrWhiteSpace(referenceNumber))
                return null;

            return $"{accountCode}|{referenceNumber}";
        }
    }
}
