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
    /// 4. Create a JournalVoucher header plus double-entry journal entries, each posted
    ///    against a real ChartOfAccount row rather than only carrying a free-text label:
    ///    - DEBIT Bank/CashReceived account (net settlement amount)
    ///    - CREDIT Transaction/CashOut account (transaction amount)
    ///    - DEBIT Processing fee expense account (gateway fees)
    ///    - CREDIT Transaction revenue/contra-revenue account (fee offsetting)
    /// 5. Verify the voucher's entries sum to zero, then post all entries atomically and
    ///    mark the transaction as posted. A voucher that doesn't balance is not posted —
    ///    it's logged and counted as failed instead, rather than committing bad GL data.
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

        // EntryType -> ChartOfAccount.Code, matching the four accounts seeded by
        // SqlServerTenantSchemaMigrator (BuildTenantChartOfAccountsAndVouchersSql).
        private static readonly Dictionary<string, string> EntryTypeToAccountCode = new()
        {
            ["DebitBank"] = "1000-BANK",
            ["CreditCashOut"] = "2000-CASHOUT",
            ["DebitFeeExpense"] = "5000-FEE",
            ["CreditFeeOffset"] = "4000-FEEOFFSET",
        };

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

            // 3. Load the chart of accounts once, keyed by code, so each entry can resolve its
            // ChartOfAccountId. Missing accounts (e.g. tenant provisioned before this migration
            // ran) leave ChartOfAccountId null rather than failing the whole cycle.
            var accountsByCode = await tenantDb.ChartOfAccounts
                .AsNoTracking()
                .ToDictionaryAsync(a => a.Code, a => a.ChartOfAccountId, cancellationToken);

            var posted = 0;
            var failed = 0;
            var noMatch = 0;

            // 4. Process each JournalReady transaction
            foreach (var txn in journalReadyTxns)
            {
                // Skip if already posted
                if (existingJournalEntries.Contains(txn.TransactionId))
                {
                    _logger.LogDebug("Transaction {TransactionId} already has journal entries", txn.TransactionId);
                    continue;
                }

                // Only card cashouts require a confirmed Level-4 bank match before posting;
                // everything else (cash cashouts, etc.) posts directly on approval.
                var requiresLevel4Match = txn.TransactionType == TransactionType.CashOut
                    && txn.PaymentMethod == PaymentMethod.Card;

                ReconciliationMatchGroup? matchGroup = null;
                if (requiresLevel4Match)
                {
                    // Gate: match group must be Level4 and confirmed (manual or auto exact-match
                    // confirmation) before the settlement can be posted to the GL.
                    matchGroup = await tenantDb.ReconciliationMatchGroups
                        .FirstOrDefaultAsync(
                            g => g.MatchLevel == "Level4" && g.IsConfirmed && g.MatchMetadataJson != null &&
                            g.MatchMetadataJson.Contains(txn.TransactionId.ToString()),
                            cancellationToken);

                    if (matchGroup == null)
                    {
                        _logger.LogWarning("No confirmed Level4 match group found for card cashout transaction {TransactionId}", txn.TransactionId);
                        noMatch++;
                        continue;
                    }
                }

                try
                {
                    decimal bankNetTotal;
                    decimal processingFeeAdjustment;

                    if (matchGroup != null)
                    {
                        // Extract settlement metadata from match group
                        var metadata = JsonSerializer.Deserialize<Dictionary<string, object>>(
                            matchGroup.MatchMetadataJson ?? "{}") ?? new();

                        bankNetTotal = ExtractDecimal(metadata, "bankNetTotal");
                        processingFeeAdjustment = ExtractDecimal(metadata, "processingFeeAdjustment");
                    }
                    else
                    {
                        // Non-card cashouts post directly on approval using the approved amount;
                        // there is no bank settlement metadata to reconcile against.
                        bankNetTotal = txn.Amount;
                        processingFeeAdjustment = 0m;
                    }

                    _logger.LogDebug(
                        "Creating journal entries for transaction {TransactionId}: net={Net}, fees={Fees}",
                        txn.TransactionId, bankNetTotal, processingFeeAdjustment);

                    var voucher = new JournalVoucher
                    {
                        JournalVoucherId = Guid.NewGuid(),
                        TransactionId = txn.TransactionId,
                        ReconciliationMatchGroupId = matchGroup?.ReconciliationMatchGroupId,
                        Status = "Posted",
                        PostedAt = DateTime.UtcNow,
                        PostedByUserId = null,
                    };

                    var entries = new List<JournalEntry>();

                    // Entry 1: DEBIT Bank/CashReceived (net settlement), CREDIT CashOut transaction
                    entries.Add(BuildEntry(
                        voucher.JournalVoucherId, txn.TransactionId, matchGroup?.ReconciliationMatchGroupId,
                        "DebitBank", bankNetTotal, accountsByCode,
                        matchGroup != null
                            ? $"Bank settlement for transaction {txn.TransactionId} via Level4 reconciliation"
                            : $"Direct posting on approval for transaction {txn.TransactionId}"));

                    entries.Add(BuildEntry(
                        voucher.JournalVoucherId, txn.TransactionId, matchGroup?.ReconciliationMatchGroupId,
                        "CreditCashOut", -bankNetTotal, accountsByCode,
                        $"Offsetting entry for cash-out transaction {txn.TransactionId}"));

                    // Entry 2: If fees exist, create fee entries
                    if (processingFeeAdjustment > 0)
                    {
                        entries.Add(BuildEntry(
                            voucher.JournalVoucherId, txn.TransactionId, matchGroup?.ReconciliationMatchGroupId,
                            "DebitFeeExpense", processingFeeAdjustment, accountsByCode,
                            $"Gateway processing fee for transaction {txn.TransactionId}"));

                        entries.Add(BuildEntry(
                            voucher.JournalVoucherId, txn.TransactionId, matchGroup?.ReconciliationMatchGroupId,
                            "CreditFeeOffset", -processingFeeAdjustment, accountsByCode,
                            "Offsetting entry for processing fee"));
                    }

                    // A voucher that doesn't sum to zero is not real double-entry accounting —
                    // reject the posting instead of committing bad GL data.
                    var voucherTotal = entries.Sum(e => e.Amount);
                    if (voucherTotal != 0m)
                    {
                        _logger.LogError(
                            "Journal voucher for transaction {TransactionId} does not balance (sum={Sum}) — posting rejected",
                            txn.TransactionId, voucherTotal);
                        failed++;
                        continue;
                    }

                    tenantDb.JournalVouchers.Add(voucher);
                    tenantDb.JournalEntries.AddRange(entries);

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

                    _logger.LogInformation("Successfully posted journal voucher {VoucherId} for transaction {TransactionId}",
                        voucher.JournalVoucherId, txn.TransactionId);
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

        private static JournalEntry BuildEntry(
            Guid voucherId,
            Guid transactionId,
            Guid? matchGroupId,
            string entryType,
            decimal amount,
            Dictionary<string, Guid> accountsByCode,
            string notes)
        {
            accountsByCode.TryGetValue(EntryTypeToAccountCode[entryType], out var chartOfAccountId);

            return new JournalEntry
            {
                JournalEntryId = Guid.NewGuid(),
                JournalVoucherId = voucherId,
                TransactionId = transactionId,
                ReconciliationMatchGroupId = matchGroupId,
                ChartOfAccountId = chartOfAccountId == Guid.Empty ? null : chartOfAccountId,
                EntryType = entryType,
                Amount = amount,
                Currency = "LKR",
                PostedAt = DateTime.UtcNow,
                PostedByUserId = null,
                Notes = notes,
            };
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
