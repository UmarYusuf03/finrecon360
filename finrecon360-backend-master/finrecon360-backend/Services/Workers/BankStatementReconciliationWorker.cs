using System.Text.Json;
using finrecon360_backend.Data;
using finrecon360_backend.Models;
using finrecon360_backend.Services;
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
    /// BANK candidates are scoped to the transaction's BankAccountId when set.
    /// When both are found with matching amounts the worker *proposes* a match group.
    /// It does not confirm it and does not promote the transaction. A person accepts the match on
    /// the matcher screen, and that confirmation is what moves the transaction to
    /// <c>JournalReady</c>.
    /// </summary>
    ///
    /// When both records are found with matching amounts, a pending Level4 match group is
    /// created and linked to the transaction. The match still requires a human to confirm it
    /// via the confirmation screen (<see cref="ReconciliationMatchConfirmationService"/>) — only
    /// confirmation promotes the transaction to <c>JournalReady</c>.
    ///
    /// When there is a variance or ambiguity (2+ candidates within tolerance of each other),
    /// the transaction remains in <c>NeedsBankMatch</c> and an exception is counted so it
    /// surfaces in the unmatched queue for manual review.
    /// </summary>
    public class BankStatementReconciliationWorker
    {
        private readonly ILogger<BankStatementReconciliationWorker> _logger;
        private readonly IReconciliationSettingsProvider _settingsProvider;

        public BankStatementReconciliationWorker(
            ILogger<BankStatementReconciliationWorker> logger,
            IReconciliationSettingsProvider settingsProvider)
        {
            _logger = logger;
            _settingsProvider = settingsProvider;
        }

        public async Task<BankReconciliationResult> ExecuteAsync(
            Guid tenantId,
            TenantDbContext tenantDb,
            CancellationToken ct = default)
        {
            var settings = await _settingsProvider.GetAsync(tenantDb, ct);
            var amountTolerance = settings.AmountTolerance;

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

            var linkedRecordIds = await tenantDb.ReconciliationMatchedRecords
                .AsNoTracking()
                .Select(r => r.ImportedNormalizedRecordId)
                .ToListAsync(ct);
            var linkedRecordIdSet = linkedRecordIds.ToHashSet();

            gatewayRecords = gatewayRecords.Where(r => !linkedRecordIdSet.Contains(r.ImportedNormalizedRecordId)).ToList();

            var bankRecordsWithAccount = await (
                from record in tenantDb.ImportedNormalizedRecords
                join batch in tenantDb.ImportBatches on record.ImportBatchId equals batch.ImportBatchId
                where batch.SourceType == "BANK" && batch.Status == "COMMITTED" && record.MatchStatus != "MATCHED"
                      && !linkedRecordIdSet.Contains(record.ImportedNormalizedRecordId)
                select new { Record = record, BatchBankAccountId = batch.BankAccountId }
            ).ToListAsync(ct);

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

            var bankByKey = bankRecordsWithAccount
                .Select(x => (x.Record, x.BatchBankAccountId, Key: SettlementKeyResolver.Resolve(x.Record)))
                .Where(x => !string.IsNullOrWhiteSpace(x.Key))
                .GroupBy(x => x.Key!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(x => (x.Record, x.BatchBankAccountId)).ToList(),
                    StringComparer.OrdinalIgnoreCase);

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
                        tenantDb, txn, gatewayRecords, bankByKey, amountTolerance);

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
            Dictionary<string, List<(ImportedNormalizedRecord Record, Guid? BatchBankAccountId)>> bankByKey,
            decimal amountTolerance)
        {
            // Step 1: Find the GATEWAY record for this transaction.
            var gatewayCandidates = FindGatewayCandidates(gatewayRecords, txn, amountTolerance);

            if (gatewayCandidates.Count == 0)
            {
                _logger.LogDebug(
                    "No GATEWAY record found for transaction {TransactionId} (amount={Amount}, date={Date})",
                    txn.TransactionId, txn.Amount, txn.TransactionDate.Date);
                return MatchOutcome.NoMatch;
            }

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
            if (!bankByKey.TryGetValue(settlementKey, out var bankCandidatesWithAccount) ||
                bankCandidatesWithAccount.Count == 0)
            {
                _logger.LogDebug(
                    "No BANK record found for settlement key '{Key}' (transaction {TransactionId})",
                    settlementKey, txn.TransactionId);
                return MatchOutcome.NoMatch;
            }

            // Step 4: Scope to the transaction's bank account.
            var scopedCandidates = bankCandidatesWithAccount
                .Where(x => txn.BankAccountId == null || x.BatchBankAccountId == null || x.BatchBankAccountId == txn.BankAccountId)
                .Select(x => x.Record)
                .ToList();

            if (scopedCandidates.Count == 0)
            {
                _logger.LogDebug(
                    "BANK candidates exist for settlement key '{Key}' but none belong to transaction {TransactionId}'s bank account {BankAccountId}",
                    settlementKey, txn.TransactionId, txn.BankAccountId);
                return MatchOutcome.NoMatch;
            }

            // Step 5: Find best amount match among scoped candidates.
            var matchResult = AmountMatcher.FindBestMatch(scopedCandidates, txn.Amount, amountTolerance);

            if (matchResult.Outcome == AmountMatcher.Outcome.Ambiguous)
            {
                _logger.LogWarning(
                    "Ambiguous BANK match for transaction {TransactionId}: {Count} candidates within tolerance of amount={Amount}",
                    txn.TransactionId, matchResult.CandidateCount, txn.Amount);
                return MatchOutcome.Exception;
            }

            if (matchResult.Outcome == AmountMatcher.Outcome.NoMatch)
            {
                var firstCandidate = scopedCandidates[0];
                var variance = firstCandidate.NetAmount - txn.Amount;
                _logger.LogWarning(
                    "Amount variance for transaction {TransactionId}: expected {Expected}, bank={Bank}, variance={Variance}",
                    txn.TransactionId, txn.Amount, firstCandidate.NetAmount, variance);
                return MatchOutcome.Exception;
            }

            var bankRecord = matchResult.Best!;
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

            _logger.LogInformation(
                "PROPOSED match for transaction {TransactionId}: settlement key '{Key}', match group {GroupId} — awaiting confirmation",
                txn.TransactionId, settlementKey, matchGroup.ReconciliationMatchGroupId);

            return MatchOutcome.AutoMatched;
        }

        private static List<ImportedNormalizedRecord> FindGatewayCandidates(
            List<ImportedNormalizedRecord> gatewayRecords,
            Transaction txn,
            decimal amountTolerance) =>
            gatewayRecords
                .Where(r => Math.Abs(r.NetAmount - txn.Amount) < amountTolerance
                    && r.TransactionDate.Date == txn.TransactionDate.Date)
                .ToList();

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
