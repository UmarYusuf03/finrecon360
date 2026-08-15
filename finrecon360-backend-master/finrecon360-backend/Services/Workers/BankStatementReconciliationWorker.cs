using finrecon360_backend.Data;
using finrecon360_backend.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace finrecon360_backend.Services.Workers
{
    /// <summary>
    /// Implements Expense Match (Rule 4): Approved Card Cashout ↔ Bank Statement.
    ///
    /// For each transaction in the <c>NeedsBankMatch</c> state (approved card cash-outs),
    /// the worker attempts to find a matching pair of imported records:
    ///   1. A GATEWAY record that represents the card charge (identifies the SettlementKey).
    ///   2. A BANK record that represents the corresponding bank debit (same key, same amount).
    ///
    /// When both records are found with matching amounts, the match is auto-confirmed and
    /// the transaction is promoted to <c>JournalReady</c>.
    ///
    /// When there is a variance or ambiguity, the transaction remains in <c>NeedsBankMatch</c>
    /// and an exception is counted so it surfaces in the unmatched queue for manual review.
    /// </summary>
    public class BankStatementReconciliationWorker
    {
        private readonly ILogger<BankStatementReconciliationWorker> _logger;

        // Amount tolerance for matching: records within this many currency units are considered equal.
        private const decimal AmountTolerance = 0.01m;

        public BankStatementReconciliationWorker(ILogger<BankStatementReconciliationWorker> logger)
        {
            _logger = logger;
        }

        public async Task<BankReconciliationResult> ExecuteAsync(
            Guid tenantId,
            TenantDbContext tenantDb,
            CancellationToken ct = default)
        {
            // 1. Load all card cash-out transactions waiting for a bank match.
            var needsMatchTransactions = await tenantDb.Transactions
                .Where(t =>
                    t.TransactionState == TransactionState.NeedsBankMatch &&
                    t.TransactionType == TransactionType.CashOut &&
                    t.PaymentMethod == PaymentMethod.Card)
                .ToListAsync(ct);

            var totalCount = needsMatchTransactions.Count;
            if (totalCount == 0)
            {
                return new BankReconciliationResult(0, 0, 0, 0);
            }

            _logger.LogInformation(
                "BankStatementReconciliationWorker: tenant {TenantId} — {Count} transactions in NeedsBankMatch",
                tenantId, totalCount);

            // 2. Load all COMMITTED GATEWAY records that are still PENDING a match.
            var gatewayRecords = await QueryCommittedBySourceAsync(tenantDb, "GATEWAY", ct);

            // 3. Load all COMMITTED BANK records that are still PENDING a match.
            var bankRecords = await QueryCommittedBySourceAsync(tenantDb, "BANK", ct);

            // 4. Build BANK lookup by SettlementKey for O(1) lookups.
            //    SettlementKey = "AccountCode|ReferenceNumber" (case-insensitive).
            var bankByKey = bankRecords
                .Where(r => !string.IsNullOrWhiteSpace(r.AccountCode) &&
                            !string.IsNullOrWhiteSpace(r.ReferenceNumber))
                .GroupBy(
                    r => BuildSettlementKey(r.AccountCode!, r.ReferenceNumber!),
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            var autoMatched = 0;
            var exceptions = 0;
            var noMatch = 0;

            foreach (var txn in needsMatchTransactions)
            {
                try
                {
                    var outcome = TryMatchTransaction(
                        tenantDb, txn, gatewayRecords, bankByKey);

                    switch (outcome)
                    {
                        case MatchOutcome.AutoMatched:
                            autoMatched++;
                            break;
                        case MatchOutcome.Exception:
                            exceptions++;
                            break;
                        case MatchOutcome.NoMatch:
                            noMatch++;
                            break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "BankStatementReconciliationWorker: unhandled error matching transaction {TransactionId}",
                        txn.TransactionId);
                    exceptions++;
                }
            }

            await tenantDb.SaveChangesAsync(ct);

            _logger.LogInformation(
                "BankStatementReconciliationWorker: tenant {TenantId} — matched={Matched}, exceptions={Exceptions}, noMatch={NoMatch}",
                tenantId, autoMatched, exceptions, noMatch);

            return new BankReconciliationResult(totalCount, autoMatched, exceptions, noMatch);
        }

        // ── Private helpers ───────────────────────────────────────────────────────────

        private MatchOutcome TryMatchTransaction(
            TenantDbContext tenantDb,
            Transaction txn,
            List<ImportedNormalizedRecord> gatewayRecords,
            Dictionary<string, List<ImportedNormalizedRecord>> bankByKey)
        {
            // Step 1: Find the GATEWAY record for this transaction (same date + exact amount).
            var gatewayRecord = gatewayRecords.FirstOrDefault(g =>
                Math.Abs(g.NetAmount - txn.Amount) < AmountTolerance &&
                g.TransactionDate.Date == txn.TransactionDate.Date);

            if (gatewayRecord == null)
            {
                // No GATEWAY record found at all — the gateway file may not have been imported yet,
                // or this transaction's date/amount doesn't match any gateway line.
                _logger.LogDebug(
                    "No GATEWAY record found for transaction {TransactionId} (amount={Amount}, date={Date})",
                    txn.TransactionId, txn.Amount, txn.TransactionDate.Date);
                return MatchOutcome.NoMatch;
            }

            // Step 2: Validate the GATEWAY record has the fields needed to build a settlement key.
            if (string.IsNullOrWhiteSpace(gatewayRecord.AccountCode) ||
                string.IsNullOrWhiteSpace(gatewayRecord.ReferenceNumber))
            {
                // Missing settlement key — this is a data quality exception.
                _logger.LogWarning(
                    "GATEWAY record {RecordId} is missing AccountCode or ReferenceNumber — cannot build settlement key",
                    gatewayRecord.ImportedNormalizedRecordId);
                return MatchOutcome.Exception;
            }

            var settlementKey = BuildSettlementKey(gatewayRecord.AccountCode, gatewayRecord.ReferenceNumber);
            gatewayRecord.SettlementKey = settlementKey;

            // Step 3: Look up the BANK record(s) with the same settlement key.
            if (!bankByKey.TryGetValue(settlementKey, out var bankCandidates) ||
                bankCandidates.Count == 0)
            {
                // BANK record not yet available — likely the bank statement hasn't been imported for this date.
                _logger.LogDebug(
                    "No BANK record found for settlement key '{Key}' (transaction {TransactionId})",
                    settlementKey, txn.TransactionId);
                return MatchOutcome.NoMatch;
            }

            // Step 4: Among the BANK candidates, find one with a matching amount.
            var bankRecord = bankCandidates.FirstOrDefault(b =>
                Math.Abs(b.NetAmount - txn.Amount) < AmountTolerance);

            if (bankRecord == null)
            {
                // Amount variance — records exist but amounts differ.
                var firstCandidate = bankCandidates[0];
                var variance = firstCandidate.NetAmount - txn.Amount;
                _logger.LogWarning(
                    "Amount variance for transaction {TransactionId}: expected {Expected}, bank={Bank}, variance={Variance}",
                    txn.TransactionId, txn.Amount, firstCandidate.NetAmount, variance);
                return MatchOutcome.Exception;
            }

            // Step 5: Create the Level4 match group and link both records.
            var matchGroup = new ReconciliationMatchGroup
            {
                ReconciliationMatchGroupId = Guid.NewGuid(),
                MatchLevel = "Level4",
                SettlementKey = settlementKey,
                // Exact-match auto-confirmation: no human review needed.
                IsConfirmed = true,
                ConfirmedAt = DateTime.UtcNow,
                MatchedAmount = txn.Amount,
                Variance = 0m,
                Status = "Confirmed",
                CreatedAt = DateTime.UtcNow,
            };
            tenantDb.ReconciliationMatchGroups.Add(matchGroup);

            tenantDb.ReconciliationMatchedRecords.Add(new ReconciliationMatchedRecord
            {
                ReconciliationMatchedRecordId = Guid.NewGuid(),
                ReconciliationMatchGroupId = matchGroup.ReconciliationMatchGroupId,
                ImportedNormalizedRecordId = gatewayRecord.ImportedNormalizedRecordId,
                SourceType = "GATEWAY",
                LinkedAt = DateTime.UtcNow,
            });

            tenantDb.ReconciliationMatchedRecords.Add(new ReconciliationMatchedRecord
            {
                ReconciliationMatchedRecordId = Guid.NewGuid(),
                ReconciliationMatchGroupId = matchGroup.ReconciliationMatchGroupId,
                ImportedNormalizedRecordId = bankRecord.ImportedNormalizedRecordId,
                SourceType = "BANK",
                LinkedAt = DateTime.UtcNow,
            });

            // Step 6: Mark imported records as matched so they are not re-matched in future runs.
            gatewayRecord.MatchStatus = "MATCHED";
            bankRecord.MatchStatus = "MATCHED";

            // Step 7: Promote the transaction — unlocks journal posting.
            txn.TransactionState = TransactionState.JournalReady;
            txn.UpdatedAt = DateTime.UtcNow;

            _logger.LogInformation(
                "AUTO-MATCHED transaction {TransactionId}: settlement key '{Key}', match group {GroupId}",
                txn.TransactionId, settlementKey, matchGroup.ReconciliationMatchGroupId);

            return MatchOutcome.AutoMatched;
        }

        private static Task<List<ImportedNormalizedRecord>> QueryCommittedBySourceAsync(
            TenantDbContext tenantDb,
            string sourceType,
            CancellationToken ct) =>
            (from record in tenantDb.ImportedNormalizedRecords
             join batch in tenantDb.ImportBatches
                 on record.ImportBatchId equals batch.ImportBatchId
             where batch.SourceType == sourceType
                && batch.Status == "COMMITTED"
                && record.MatchStatus == "PENDING"
             select record).ToListAsync(ct);

        private static string BuildSettlementKey(string accountCode, string referenceNumber) =>
            $"{accountCode}|{referenceNumber}";

        // ── Result types ──────────────────────────────────────────────────────────────

        private enum MatchOutcome
        {
            AutoMatched,
            Exception,
            NoMatch,
        }
    }

    /// <summary>
    /// Summary counters returned by <see cref="BankStatementReconciliationWorker.ExecuteAsync"/>.
    /// </summary>
    public record BankReconciliationResult(
        int NeedsBankMatchCount,
        int AutoMatchedCount,
        int ExceptionCount,
        int NoMatchCount);
}
