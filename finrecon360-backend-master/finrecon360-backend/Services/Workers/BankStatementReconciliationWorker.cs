using System.Text.Json;
using finrecon360_backend.Data;
using finrecon360_backend.Models;
using finrecon360_backend.Services.Reconciliation;
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
    /// When both are found with matching amounts the worker *proposes* a match group and stops.
    /// It does not confirm it and does not promote the transaction. A person accepts the match on
    /// the matcher screen, and that confirmation is what moves the transaction to
    /// <c>JournalReady</c> — see ReconciliationMatchConfirmationService.ConfirmMatchAsync.
    ///
    /// WHY the worker no longer auto-confirms: IsConfirmed is the audit record of a human accepting
    /// a match, and journal posting is gated on it. A worker setting it means money reaches the
    /// ledger with nobody having looked, which is the control this product exists to provide.
    /// The proposal is still recorded as AutoMatched so the UI can show it was machine-suggested.
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

            // 2/3. Load COMMITTED GATEWAY and BANK records still eligible for a Level-4 match.
            var gatewayRecords = await QueryMatchableBySourceAsync(tenantDb, "GATEWAY", ct);
            var bankRecords = await QueryMatchableBySourceAsync(tenantDb, "BANK", ct);

            // Records already linked into a match group must not be matched a second time. This
            // replaces the previous "MatchStatus == PENDING" guard, which stopped working once
            // Level 3 began marking gateway rows SALES_VERIFIED — those are precisely the rows that
            // should reach Level 4, and the old filter excluded every one of them.
            var linkedRecordIds = await tenantDb.ReconciliationMatchedRecords
                .AsNoTracking()
                .Select(r => r.ImportedNormalizedRecordId)
                .ToListAsync(ct);
            var linkedRecordIdSet = linkedRecordIds.ToHashSet();

            gatewayRecords = gatewayRecords.Where(r => !linkedRecordIdSet.Contains(r.ImportedNormalizedRecordId)).ToList();
            bankRecords = bankRecords.Where(r => !linkedRecordIdSet.Contains(r.ImportedNormalizedRecordId)).ToList();

            // Transactions that already have a proposed or confirmed Level-4 group must not get a
            // second one — re-proposing every cycle would bury the matcher screen in duplicates.
            var existingLevel4Groups = await tenantDb.ReconciliationMatchGroups
                .AsNoTracking()
                .Where(g => g.MatchLevel == "Level4" && g.MatchMetadataJson != null)
                .Select(g => g.MatchMetadataJson!)
                .ToListAsync(ct);

            var alreadyProposedTransactionIds = existingLevel4Groups
                .Select(MatchGroupMetadata.TryParse)
                .Where(m => m?.TransactionId is not null)
                .Select(m => m!.TransactionId!.Value)
                .ToHashSet();

            // 4. Build BANK lookup by settlement key for O(1) lookups.
            var bankByKey = bankRecords
                .Select(r => new { Record = r, Key = SettlementKeyResolver.Resolve(r) })
                .Where(x => !string.IsNullOrWhiteSpace(x.Key))
                .GroupBy(x => x.Key!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Record).ToList(), StringComparer.OrdinalIgnoreCase);

            var autoMatched = 0;
            var exceptions = 0;
            var noMatch = 0;

            foreach (var txn in needsMatchTransactions)
            {
                if (alreadyProposedTransactionIds.Contains(txn.TransactionId))
                {
                    _logger.LogDebug("Transaction {TransactionId} already has a Level-4 match group", txn.TransactionId);
                    continue;
                }

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
                "BankStatementReconciliationWorker: tenant {TenantId} — proposed={Matched}, exceptions={Exceptions}, noMatch={NoMatch}",
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
            // Step 1: Find the GATEWAY record for this transaction. Reference number is the strong
            // signal; date plus amount is a weak fallback, since two card cashouts of the same value
            // on the same day are routine for a retailer.
            var gatewayCandidates = FindGatewayCandidates(gatewayRecords, txn);

            if (gatewayCandidates.Count == 0)
            {
                // No GATEWAY record found at all — the gateway file may not have been imported yet,
                // or this transaction's date/amount doesn't match any gateway line.
                _logger.LogDebug(
                    "No GATEWAY record found for transaction {TransactionId} (amount={Amount}, date={Date})",
                    txn.TransactionId, txn.Amount, txn.TransactionDate.Date);
                return MatchOutcome.NoMatch;
            }

            // WHY ambiguity is an exception rather than a guess: picking the first of several equally
            // plausible candidates silently settles a transaction against the wrong payment, and
            // nothing downstream can detect it. A person resolving an exception is recoverable;
            // a wrong match posted to the ledger is not.
            if (gatewayCandidates.Count > 1)
            {
                _logger.LogWarning(
                    "Ambiguous GATEWAY match for transaction {TransactionId}: {Count} candidates",
                    txn.TransactionId, gatewayCandidates.Count);

                tenantDb.ReconciliationEvents.Add(new ReconciliationEvent
                {
                    ReconciliationEventId = Guid.NewGuid(),
                    ImportBatchId = gatewayCandidates[0].ImportBatchId,
                    ImportedNormalizedRecordId = gatewayCandidates[0].ImportedNormalizedRecordId,
                    EventType = "ManualReview",
                    Stage = "Level4",
                    SourceType = "GATEWAY",
                    Status = "RequiresReview",
                    DetailJson = JsonSerializer.Serialize(new
                    {
                        reason = "Multiple gateway records match this transaction",
                        transactionId = txn.TransactionId,
                        candidateCount = gatewayCandidates.Count,
                        candidateRecordIds = gatewayCandidates.Select(c => c.ImportedNormalizedRecordId).ToList(),
                        amount = txn.Amount
                    })
                });

                return MatchOutcome.Exception;
            }

            var gatewayRecord = gatewayCandidates[0];

            // Step 2: Validate the GATEWAY record can produce a settlement key.
            var settlementKey = SettlementKeyResolver.Resolve(gatewayRecord);
            if (string.IsNullOrWhiteSpace(settlementKey))
            {
                _logger.LogWarning(
                    "GATEWAY record {RecordId} has no SettlementId, AccountCode or ReferenceNumber — cannot build settlement key",
                    gatewayRecord.ImportedNormalizedRecordId);
                return MatchOutcome.Exception;
            }

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

            // Step 5: Propose the Level4 match group and link both records. Unconfirmed by design.
            var processingFee = gatewayRecord.ProcessingFee ?? 0m;

            var matchGroup = new ReconciliationMatchGroup
            {
                ReconciliationMatchGroupId = Guid.NewGuid(),
                ImportBatchId = gatewayRecord.ImportBatchId,
                MatchLevel = "Level4",
                SettlementKey = settlementKey,
                IsConfirmed = false,
                ConfirmedByUserId = null,
                ConfirmedAt = null,
                IsJournalPosted = false,
                MatchedAmount = bankRecord.NetAmount,
                Variance = Math.Abs(bankRecord.NetAmount - txn.Amount),
                Status = "Pending",
                CreatedAt = DateTime.UtcNow,
                // Same contract the posting worker reads, so the amounts it journals come from here.
                MatchMetadataJson = new MatchGroupMetadata
                {
                    TransactionId = txn.TransactionId,
                    SettlementKey = settlementKey,
                    GatewayBankSession = $"{gatewayRecord.ImportedNormalizedRecordId}_{bankRecord.ImportedNormalizedRecordId}",
                    GatewayNetTotal = gatewayRecord.NetAmount,
                    BankNetTotal = bankRecord.NetAmount,
                    ProcessingFeeTotal = processingFee,
                    Variance = Math.Abs(bankRecord.NetAmount - txn.Amount),
                    AutoMatched = true,
                    GatewayRecordCount = 1,
                    BankRecordCount = 1,
                    MatchedAt = DateTime.UtcNow
                }.Serialize()
            };
            tenantDb.ReconciliationMatchGroups.Add(matchGroup);

            tenantDb.ReconciliationMatchedRecords.Add(new ReconciliationMatchedRecord
            {
                ReconciliationMatchedRecordId = Guid.NewGuid(),
                ReconciliationMatchGroupId = matchGroup.ReconciliationMatchGroupId,
                ImportedNormalizedRecordId = gatewayRecord.ImportedNormalizedRecordId,
                SourceType = "GATEWAY",
                MatchAmount = gatewayRecord.NetAmount,
                LinkedAt = DateTime.UtcNow,
            });

            tenantDb.ReconciliationMatchedRecords.Add(new ReconciliationMatchedRecord
            {
                ReconciliationMatchedRecordId = Guid.NewGuid(),
                ReconciliationMatchGroupId = matchGroup.ReconciliationMatchGroupId,
                ImportedNormalizedRecordId = bankRecord.ImportedNormalizedRecordId,
                SourceType = "BANK",
                MatchAmount = bankRecord.NetAmount,
                LinkedAt = DateTime.UtcNow,
            });

            // Step 6: Records are NOT marked MATCHED here and the transaction is NOT promoted.
            // Both happen on confirmation — MATCHED means a human accepted this settlement.
            // Re-matching is prevented by the linked-record and proposed-transaction guards above.

            _logger.LogInformation(
                "PROPOSED match for transaction {TransactionId}: settlement key '{Key}', match group {GroupId} — awaiting confirmation",
                txn.TransactionId, settlementKey, matchGroup.ReconciliationMatchGroupId);

            return MatchOutcome.AutoMatched;
        }

        /// <summary>
        /// Returns every gateway record that could be this transaction, rather than the first one
        /// that fits, so the caller can refuse to guess between them.
        ///
        /// NOTE: date plus amount is a weak key. The strong key would be a reference number, but
        /// Transaction.ReferenceNumber was removed from the model on main even though the column
        /// still exists in tenant databases. Until that is settled, ambiguity is common and is
        /// surfaced as an exception rather than resolved arbitrarily.
        /// </summary>
        private static List<ImportedNormalizedRecord> FindGatewayCandidates(
            List<ImportedNormalizedRecord> gatewayRecords,
            Transaction txn) =>
            gatewayRecords
                .Where(r => Math.Abs(r.NetAmount - txn.Amount) < AmountTolerance
                    && r.TransactionDate.Date == txn.TransactionDate.Date)
                .ToList();

        /// <summary>
        /// Committed records from the given source that have not already reached a final matched
        /// state. Deliberately not filtered to MatchStatus == "PENDING": Level 3 promotes gateway
        /// rows to SALES_VERIFIED, and those are exactly the rows Level 4 needs to see.
        /// </summary>
        private static Task<List<ImportedNormalizedRecord>> QueryMatchableBySourceAsync(
            TenantDbContext tenantDb,
            string sourceType,
            CancellationToken ct) =>
            (from record in tenantDb.ImportedNormalizedRecords
             join batch in tenantDb.ImportBatches
                 on record.ImportBatchId equals batch.ImportBatchId
             where batch.SourceType == sourceType
                && batch.Status == "COMMITTED"
                && record.MatchStatus != "MATCHED"
             select record).ToListAsync(ct);

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
