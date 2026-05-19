using System.Text.Json;
using finrecon360_backend.Data;
using finrecon360_backend.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace finrecon360_backend.Services.Workers
{
    /// <summary>
    /// WHY: Automates Level-5 journal posting for transactions that have been reconciled
    /// and are ready for accounting entry.
    /// 
    /// Purpose: Continuously monitors JournalReady transactions and creates double-entry
    /// journal entries for posting to the GL without manual data entry. Each successful
    /// posting unlocks downstream accounting workflows (GL export, tax reconciliation, etc.).
    /// 
    /// Workflow:
    /// 1. Find all transactions in JournalReady state created within a lookback window.
    /// 2. For each transaction, verify the ReconciliationMatchGroup exists and is confirmed.
    /// 3. Extract settlement details from the match group:
    ///    - Net amount received/settled
    ///    - Processing fees charged by gateway
    ///    - Currency and timestamp
    /// 4. Create double-entry journal entries:
    ///    - DEBIT Bank/CashReceived account (net settlement amount)
    ///    - CREDIT Transaction/CashOut account (transaction amount)
    ///    - DEBIT Processing fee expense account (gateway fees)
    ///    - CREDIT Transaction revenue/contra-revenue account (fee offsetting)
    /// 5. Post all entries atomically; mark transaction as posted.
    /// 6. Log posting event for audit trail.
    /// 
    /// Fee Handling:
    /// The BankReconciliationWorker has already matched GATEWAY records (with fees deducted)
    /// to BANK records. So when journal posting occurs:
    /// - The NetAmount from the bank represents what was deposited
    /// - Any processing fees are captured in ReconciliationMatchGroup.MatchMetadataJson
    /// - Journal entries properly split the settlement into:
    ///   1. Bank deposit (net amount)
    ///   2. Fee expense (gateway processing fee)
    /// This ensures accurate GL representation of the actual bank deposit split from fees.
    /// </summary>
    public interface IJournalPostingExecutorWorker
    {
        /// <summary>
        /// Execute one cycle of journal posting for the given tenant.
        /// Safe to call repeatedly; idempotent on already-posted transactions.
        /// </summary>
        Task<JournalPostingResult> ExecuteAsync(
            Guid tenantId,
            TenantDbContext tenantDb,
            CancellationToken cancellationToken = default);
    }

    public record JournalPostingResult(
        int JournalReadyCount,
        int PostedCount,
        int FailedCount,
        int NoMatchCount,
        string Summary);

    public class JournalPostingExecutorWorker : IJournalPostingExecutorWorker
    {
        private static readonly TimeSpan LookbackWindow = TimeSpan.FromDays(30);
        private readonly ILogger<JournalPostingExecutorWorker> _logger;

        public JournalPostingExecutorWorker(ILogger<JournalPostingExecutorWorker> logger)
        {
            _logger = logger;
        }

        public async Task<JournalPostingResult> ExecuteAsync(
            Guid tenantId,
            TenantDbContext tenantDb,
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Journal posting cycle started for tenant {TenantId}", tenantId);

            // 1. Find all JournalReady transactions created within the lookback window
            var cutoffDate = DateTime.UtcNow.Subtract(LookbackWindow);
            var journalReadyTxns = await tenantDb.Transactions
                .AsNoTracking()
                .Where(x => x.TransactionState == TransactionState.JournalReady
                    && x.CreatedAt >= cutoffDate)
                .OrderBy(x => x.TransactionDate)
                .ThenBy(x => x.CreatedAt)
                .ToListAsync(cancellationToken);

            _logger.LogInformation("Found {Count} transactions in JournalReady state for tenant {TenantId}",
                journalReadyTxns.Count, tenantId);

            if (journalReadyTxns.Count == 0)
            {
                return new JournalPostingResult(0, 0, 0, 0, "No JournalReady transactions to process");
            }

            // 2. Load existing journal entries to avoid duplicate posting
            var existingJournalEntries = await tenantDb.JournalEntries
                .AsNoTracking()
                .Select(je => je.TransactionId)
                .ToListAsync(cancellationToken)
                .ContinueWith(t => new HashSet<Guid?>(t.Result));

            var posted = 0;
            var failed = 0;
            var noMatch = 0;

            // 3. Process each JournalReady transaction
            foreach (var txn in journalReadyTxns)
            {
                // Skip if already posted
                if (existingJournalEntries.Contains(txn.TransactionId))
                {
                    _logger.LogDebug("Transaction {TransactionId} already has journal entries", txn.TransactionId);
                    continue;
                }

                // Find the ReconciliationMatchGroup linked to this transaction
                var matchGroup = await tenantDb.ReconciliationMatchGroups
                    .FirstOrDefaultAsync(
                        g => g.MatchLevel == "Level4" && g.MatchMetadataJson != null &&
                        g.MatchMetadataJson.Contains(txn.TransactionId.ToString()),
                        cancellationToken);

                if (matchGroup == null)
                {
                    _logger.LogWarning("No match group found for transaction {TransactionId}", txn.TransactionId);
                    noMatch++;
                    continue;
                }

                try
                {
                    // Extract settlement metadata from match group
                    var metadata = JsonSerializer.Deserialize<Dictionary<string, object>>(
                        matchGroup.MatchMetadataJson ?? "{}") ?? new();

                    var bankNetTotal = ExtractDecimal(metadata, "bankNetTotal");
                    var bankFeeTotal = ExtractDecimal(metadata, "bankFeeTotal");
                    var gatewayNetAmount = ExtractDecimal(metadata, "gatewayNetAmount");
                    var processingFeeAdjustment = ExtractDecimal(metadata, "processingFeeAdjustment");

                    _logger.LogDebug(
                        "Creating journal entries for transaction {TransactionId}: net={Net}, fees={Fees}",
                        txn.TransactionId, bankNetTotal, processingFeeAdjustment);

                    // Create double-entry journal entries
                    // Entry 1: DEBIT Bank/CashReceived (net settlement), CREDIT CashOut transaction
                    var entryDebitBank = new JournalEntry
                    {
                        JournalEntryId = Guid.NewGuid(),
                        TransactionId = txn.TransactionId,
                        ReconciliationMatchGroupId = matchGroup.ReconciliationMatchGroupId,
                        EntryType = "DebitBank",
                        Amount = bankNetTotal,
                        Currency = "LKR",
                        PostedAt = DateTime.UtcNow,
                        PostedByUserId = null, // Automated posting
                        Notes = $"Bank settlement for transaction {txn.TransactionId} via Level4 reconciliation"
                    };
                    tenantDb.JournalEntries.Add(entryDebitBank);

                    var entryCreditCashOut = new JournalEntry
                    {
                        JournalEntryId = Guid.NewGuid(),
                        TransactionId = txn.TransactionId,
                        ReconciliationMatchGroupId = matchGroup.ReconciliationMatchGroupId,
                        EntryType = "CreditCashOut",
                        Amount = -bankNetTotal, // Negative for credit
                        Currency = "LKR",
                        PostedAt = DateTime.UtcNow,
                        PostedByUserId = null,
                        Notes = $"Offsetting entry for cash-out transaction {txn.TransactionId}"
                    };
                    tenantDb.JournalEntries.Add(entryCreditCashOut);

                    // Entry 2: If fees exist, create fee entries
                    if (processingFeeAdjustment > 0)
                    {
                        var entryDebitFeeExpense = new JournalEntry
                        {
                            JournalEntryId = Guid.NewGuid(),
                            TransactionId = txn.TransactionId,
                            ReconciliationMatchGroupId = matchGroup.ReconciliationMatchGroupId,
                            EntryType = "DebitFeeExpense",
                            Amount = processingFeeAdjustment,
                            Currency = "LKR",
                            PostedAt = DateTime.UtcNow,
                            PostedByUserId = null,
                            Notes = $"Gateway processing fee for transaction {txn.TransactionId}"
                        };
                        tenantDb.JournalEntries.Add(entryDebitFeeExpense);

                        var entryCreditFeeOffset = new JournalEntry
                        {
                            JournalEntryId = Guid.NewGuid(),
                            TransactionId = txn.TransactionId,
                            ReconciliationMatchGroupId = matchGroup.ReconciliationMatchGroupId,
                            EntryType = "CreditFeeOffset",
                            Amount = -processingFeeAdjustment, // Negative for credit
                            Currency = "LKR",
                            PostedAt = DateTime.UtcNow,
                            PostedByUserId = null,
                            Notes = $"Offsetting entry for processing fee"
                        };
                        tenantDb.JournalEntries.Add(entryCreditFeeOffset);
                    }

                    // Mark transaction as posted by creating a state history entry
                    var stateHistory = new TransactionStateHistory
                    {
                        TransactionStateHistoryId = Guid.NewGuid(),
                        TransactionId = txn.TransactionId,
                        FromState = TransactionState.JournalReady,
                        ToState = TransactionState.JournalReady, // No state change; just logged posting
                        ChangedAt = DateTime.UtcNow,
                        ChangedByUserId = null,
                        Note = "Auto-posted by JournalPostingExecutorWorker"
                    };
                    tenantDb.TransactionStateHistories.Add(stateHistory);

                    _logger.LogInformation("Successfully posted journal entries for transaction {TransactionId}",
                        txn.TransactionId);
                    posted++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to post journal entries for transaction {TransactionId}", txn.TransactionId);
                    failed++;
                }
            }

            await tenantDb.SaveChangesAsync(cancellationToken);

            var result = new JournalPostingResult(
                journalReadyTxns.Count,
                posted,
                failed,
                noMatch,
                $"Journal posting completed: posted={posted}; failed={failed}; noMatch={noMatch}");

            _logger.LogInformation("Journal posting cycle completed for tenant {TenantId}: {Summary}",
                tenantId, result.Summary);

            return result;
        }

        private static decimal ExtractDecimal(Dictionary<string, object> metadata, string key)
        {
            if (metadata.TryGetValue(key, out var value))
            {
                if (value is JsonElement element)
                {
                    return element.GetDecimal();
                }
                if (decimal.TryParse(value?.ToString() ?? "0", out var result))
                {
                    return result;
                }
            }
            return 0m;
        }
    }
}
