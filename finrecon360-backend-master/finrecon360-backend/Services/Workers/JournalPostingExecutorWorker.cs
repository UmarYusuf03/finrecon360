using System.Text.Json;
using finrecon360_backend.Data;
using finrecon360_backend.Models;
using finrecon360_backend.Services.Reconciliation;
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

                // WHY the branch: only card cashouts settle through a bank statement, so only they
                // need a match group. Cash posts on approval — that is the documented rule, and
                // requiring a Level-4 group for every transaction is what previously left every cash
                // transaction stranded in JournalReady, unpostable, forever.
                var requiresBankSettlement =
                    txn.TransactionType == TransactionType.CashOut && txn.PaymentMethod == PaymentMethod.Card;

                ReconciliationMatchGroup? matchGroup = null;

                if (requiresBankSettlement)
                {
                    matchGroup = await FindSettlementGroupAsync(tenantDb, txn.TransactionId, cancellationToken);

                    if (matchGroup == null)
                    {
                        _logger.LogWarning("No match group found for card cashout {TransactionId}", txn.TransactionId);
                        noMatch++;
                        continue;
                    }

                    // The confirmation gate. A proposed match is not authority to move money in the
                    // ledger; a person accepting it is.
                    if (!matchGroup.IsConfirmed)
                    {
                        _logger.LogDebug(
                            "Match group {MatchGroupId} for transaction {TransactionId} is not confirmed yet",
                            matchGroup.ReconciliationMatchGroupId, txn.TransactionId);
                        noMatch++;
                        continue;
                    }

                    if (matchGroup.IsJournalPosted)
                    {
                        _logger.LogDebug("Match group {MatchGroupId} already posted", matchGroup.ReconciliationMatchGroupId);
                        continue;
                    }
                }

                try
                {
                    var metadata = MatchGroupMetadata.TryParse(matchGroup?.MatchMetadataJson);

                    if (requiresBankSettlement && metadata is null)
                    {
                        _logger.LogError(
                            "Match group {MatchGroupId} has unreadable metadata; refusing to post",
                            matchGroup!.ReconciliationMatchGroupId);
                        failed++;
                        continue;
                    }

                    // Cash posts at its own face value; a card cashout posts what the bank actually
                    // settled, with the gateway's fee split out so revenue and cash reconcile.
                    var settledAmount = requiresBankSettlement ? metadata!.BankNetTotal : txn.Amount;
                    var processingFeeAdjustment = requiresBankSettlement ? metadata!.ProcessingFeeTotal : 0m;

                    if (settledAmount <= 0m)
                    {
                        _logger.LogError(
                            "Refusing to post a non-positive amount {Amount} for transaction {TransactionId}",
                            settledAmount, txn.TransactionId);
                        failed++;
                        continue;
                    }

                    var bankNetTotal = settledAmount;

                    _logger.LogDebug(
                        "Creating journal entries for transaction {TransactionId}: net={Net}, fees={Fees}",
                        txn.TransactionId, bankNetTotal, processingFeeAdjustment);

                    var matchGroupId = matchGroup?.ReconciliationMatchGroupId;
                    // Transactions carry no currency column today; tenant books are LKR. Kept as a
                    // named local so multi-currency has one place to change.
                    const string currency = "LKR";

                    // Every posting is a balanced pair. The debit side differs by tender — a card
                    // cashout lands in the bank account, cash moves through the cash account — but
                    // the two sides always sum to zero.
                    var debitAccount = requiresBankSettlement ? "DebitBank" : "DebitCash";
                    var creditAccount = txn.TransactionType == TransactionType.CashOut
                        ? "CreditCashOut"
                        : "CreditCashIn";

                    var settlementNote = requiresBankSettlement
                        ? $"Bank settlement for transaction {txn.TransactionId} via Level4 reconciliation"
                        : $"Approved {txn.PaymentMethod} {txn.TransactionType} for transaction {txn.TransactionId}";

                    tenantDb.JournalEntries.Add(new JournalEntry
                    {
                        JournalEntryId = Guid.NewGuid(),
                        TransactionId = txn.TransactionId,
                        ReconciliationMatchGroupId = matchGroupId,
                        EntryType = debitAccount,
                        Amount = bankNetTotal,
                        Currency = currency,
                        PostedAt = DateTime.UtcNow,
                        PostedByUserId = null, // Automated posting
                        Notes = settlementNote
                    });

                    tenantDb.JournalEntries.Add(new JournalEntry
                    {
                        JournalEntryId = Guid.NewGuid(),
                        TransactionId = txn.TransactionId,
                        ReconciliationMatchGroupId = matchGroupId,
                        EntryType = creditAccount,
                        Amount = -bankNetTotal, // Negative for credit
                        Currency = currency,
                        PostedAt = DateTime.UtcNow,
                        PostedByUserId = null,
                        Notes = $"Offsetting entry for transaction {txn.TransactionId}"
                    });

                    // The gateway keeps its fee out of the deposit, so the ledger has to record it
                    // explicitly — otherwise revenue (gross) and cash (net) never agree.
                    if (processingFeeAdjustment > 0)
                    {
                        tenantDb.JournalEntries.Add(new JournalEntry
                        {
                            JournalEntryId = Guid.NewGuid(),
                            TransactionId = txn.TransactionId,
                            ReconciliationMatchGroupId = matchGroupId,
                            EntryType = "DebitFeeExpense",
                            Amount = processingFeeAdjustment,
                            Currency = currency,
                            PostedAt = DateTime.UtcNow,
                            PostedByUserId = null,
                            Notes = $"Gateway processing fee for transaction {txn.TransactionId}"
                        });

                        tenantDb.JournalEntries.Add(new JournalEntry
                        {
                            JournalEntryId = Guid.NewGuid(),
                            TransactionId = txn.TransactionId,
                            ReconciliationMatchGroupId = matchGroupId,
                            EntryType = "CreditFeeOffset",
                            Amount = -processingFeeAdjustment, // Negative for credit
                            Currency = currency,
                            PostedAt = DateTime.UtcNow,
                            PostedByUserId = null,
                            Notes = "Offsetting entry for processing fee"
                        });
                    }

                    // Closes the second half of the double-post hole: the manual match-group endpoint
                    // gates on this flag, and nothing used to set it after an automated posting.
                    if (matchGroup is not null)
                    {
                        matchGroup.IsJournalPosted = true;
                        matchGroup.UpdatedAt = DateTime.UtcNow;
                    }

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

        /// <summary>
        /// Finds the Level-4 group that settles this transaction. The lookup still filters in memory
        /// on the parsed metadata rather than a foreign key — the FK is a schema change scheduled
        /// separately — but it now matches on the parsed TransactionId instead of a substring search
        /// of the raw JSON, which could collide with any other GUID stored in the same blob.
        /// </summary>
        private static async Task<ReconciliationMatchGroup?> FindSettlementGroupAsync(
            TenantDbContext tenantDb,
            Guid transactionId,
            CancellationToken cancellationToken)
        {
            var candidates = await tenantDb.ReconciliationMatchGroups
                .Where(g => g.MatchLevel == "Level4"
                    && g.MatchMetadataJson != null
                    && g.MatchMetadataJson.Contains(transactionId.ToString()))
                .ToListAsync(cancellationToken);

            return candidates.FirstOrDefault(g =>
                MatchGroupMetadata.TryParse(g.MatchMetadataJson)?.TransactionId == transactionId);
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
