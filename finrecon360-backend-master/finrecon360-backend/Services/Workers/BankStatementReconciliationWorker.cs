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
    ///   2. A BANK record that represents the corresponding bank debit (same key, matching amount).
    ///
    /// BANK candidates are scoped to the transaction's BankAccountId when set.
    ///
    /// Matching runs as a three-tier waterfall on the final BANK-amount comparison, mirroring
    /// Level7's shape — a prior version of this worker auto-promoted every match regardless of
    /// how it was found, which let a bad match slip through unnoticed; this replaces that blanket
    /// policy with tiers scoped to how confidently each one reconciles, rather than removing
    /// auto-confirmation altogether:
    ///   Tier1 (ExactMatch)   — BANK.NetAmount reconciles with txn.Amount directly. Auto-confirms.
    ///   Tier2 (FeeExplained) — BANK.NetAmount only reconciles once the GATEWAY record's known
    ///                          ProcessingFee is subtracted from txn.Amount — a legitimate,
    ///                          already-captured deduction, not an unexplained discrepancy.
    ///                          Auto-confirms, with Variance recorded as the fee amount.
    ///   Tier3 (RequiresReview) — ambiguous under either basis, or neither basis reconciles.
    ///                          Stays IsConfirmed = false for a human to resolve via
    ///                          ReconciliationMatchConfirmationService, same as before.
    ///
    /// Auto-confirmation (Tier1/Tier2) also promotes the linked transaction to JournalReady via
    /// the shared CardCashoutPromoter — IsConfirmed alone doesn't move it; something has to run
    /// the promotion side effect, and that's the same helper the confirmation service uses for
    /// Tier3's human-confirmed case.
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
                    var outcome = await TryMatchTransactionAsync(
                        tenantDb, txn, gatewayRecords, bankByKey, amountTolerance, ct);

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
                "BankStatementReconciliationWorker: tenant {TenantId} — autoConfirmed={Matched}, exceptions={Exceptions}, noMatch={NoMatch}",
                tenantId, autoMatched, exceptions, noMatch);

            return new BankReconciliationResult(totalCount, autoMatched, exceptions, noMatch);
        }

        // ── Private helpers ───────────────────────────────────────────────────────────

        private async Task<MatchOutcome> TryMatchTransactionAsync(
            TenantDbContext tenantDb,
            Transaction txn,
            List<ImportedNormalizedRecord> gatewayRecords,
            Dictionary<string, List<(ImportedNormalizedRecord Record, Guid? BatchBankAccountId)>> bankByKey,
            decimal amountTolerance,
            CancellationToken ct)
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

            // Step 5: Tiered amount match — exact first, then fee-explained.
            var processingFee = gatewayRecord.ProcessingFee ?? 0m;

            var exactMatch = AmountMatcher.FindBestMatch(scopedCandidates, txn.Amount, amountTolerance);
            var feeMatch = processingFee > 0m
                ? AmountMatcher.FindBestMatch(scopedCandidates, txn.Amount - processingFee, amountTolerance)
                : new AmountMatcher.Result(AmountMatcher.Outcome.NoMatch, null, 0);

            if (exactMatch.Outcome == AmountMatcher.Outcome.Ambiguous || feeMatch.Outcome == AmountMatcher.Outcome.Ambiguous)
            {
                var ambiguous = exactMatch.Outcome == AmountMatcher.Outcome.Ambiguous ? exactMatch : feeMatch;
                _logger.LogWarning(
                    "Ambiguous BANK match for transaction {TransactionId}: {Count} candidates within tolerance of amount={Amount}",
                    txn.TransactionId, Math.Max(exactMatch.CandidateCount, feeMatch.CandidateCount), txn.Amount);

                // Tier3: a candidate exists but isn't confidently the only one — per the documented
                // tiered design (class doc above, WORKER-INTEGRATION.md "Level4 detail"), this still
                // gets routed to a human instead of being dropped with nothing for anyone to act on.
                var ambiguousBank = ambiguous.Best!;
                await AddMatchGroupAsync(
                    tenantDb, txn, gatewayRecord, ambiguousBank, settlementKey, processingFee,
                    variance: Math.Abs(ambiguousBank.NetAmount - txn.Amount),
                    ruleTriggered: "Tier3_AmbiguousBankMatch",
                    autoConfirm: false, ct);

                return MatchOutcome.Exception;
            }

            string ruleTriggered;
            ImportedNormalizedRecord bankRecord;
            decimal variance;
            bool autoConfirm;

            if (exactMatch.Outcome == AmountMatcher.Outcome.Matched)
            {
                ruleTriggered = "Tier1_ExactMatch";
                bankRecord = exactMatch.Best!;
                variance = Math.Abs(bankRecord.NetAmount - txn.Amount);
                autoConfirm = true;
            }
            else if (feeMatch.Outcome == AmountMatcher.Outcome.Matched)
            {
                ruleTriggered = "Tier2_FeeExplained";
                bankRecord = feeMatch.Best!;
                variance = processingFee; // the gap is the known fee, not an unexplained discrepancy
                autoConfirm = true;
            }
            else
            {
                // Tier3: neither basis reconciles — genuine, unexplained amount variance. Still
                // routed to a human for review rather than dropped; see the ambiguous branch above
                // for why (same tiered-design contract, same fix).
                ruleTriggered = "Tier3_RequiresReview";
                bankRecord = scopedCandidates[0];
                variance = Math.Abs(bankRecord.NetAmount - txn.Amount);
                autoConfirm = false;

                _logger.LogWarning(
                    "Amount variance for transaction {TransactionId}: expected {Expected}, bank={Bank}, variance={Variance}",
                    txn.TransactionId, txn.Amount, bankRecord.NetAmount, variance);
            }

            await AddMatchGroupAsync(
                tenantDb, txn, gatewayRecord, bankRecord, settlementKey, processingFee, variance, ruleTriggered, autoConfirm, ct);

            if (autoConfirm)
            {
                _logger.LogInformation(
                    "Level4 AUTO-CONFIRMED ({Rule}) transaction {TransactionId}: settlement key '{Key}'",
                    ruleTriggered, txn.TransactionId, settlementKey);
                return MatchOutcome.AutoMatched;
            }

            _logger.LogInformation(
                "Level4 PENDING-REVIEW ({Rule}) transaction {TransactionId}: settlement key '{Key}'",
                ruleTriggered, txn.TransactionId, settlementKey);
            return MatchOutcome.Exception;
        }

        /// <summary>
        /// Creates the match group + linked GATEWAY/BANK records for either an auto-confirmed
        /// (Tier1/Tier2) or pending-review (Tier3) outcome, and promotes the transaction only for
        /// the auto-confirmed case — a human does that via ReconciliationMatchConfirmationService
        /// for Tier3, same as ConfirmMatchAsync already does once this group exists for it to find.
        /// </summary>
        private static async Task AddMatchGroupAsync(
            TenantDbContext tenantDb,
            Transaction txn,
            ImportedNormalizedRecord gatewayRecord,
            ImportedNormalizedRecord bankRecord,
            string settlementKey,
            decimal processingFee,
            decimal variance,
            string ruleTriggered,
            bool autoConfirm,
            CancellationToken ct)
        {
            var now = DateTime.UtcNow;

            var matchGroup = new ReconciliationMatchGroup
            {
                ReconciliationMatchGroupId = Guid.NewGuid(),
                ImportBatchId = gatewayRecord.ImportBatchId,
                MatchLevel = "Level4",
                SettlementKey = settlementKey,
                IsConfirmed = autoConfirm,
                ConfirmedByUserId = null, // system auto-confirm, not a human; null either way pre-review
                ConfirmedAt = autoConfirm ? now : null,
                IsJournalPosted = false,
                MatchedAmount = bankRecord.NetAmount,
                Variance = variance,
                Status = autoConfirm ? "Confirmed" : "Pending",
                CreatedAt = now,
                MatchMetadataJson = new MatchGroupMetadata
                {
                    TransactionId = txn.TransactionId,
                    SettlementKey = settlementKey,
                    GatewayBankSession = $"{gatewayRecord.ImportedNormalizedRecordId}_{bankRecord.ImportedNormalizedRecordId}",
                    GatewayNetTotal = gatewayRecord.NetAmount,
                    BankNetTotal = bankRecord.NetAmount,
                    ProcessingFeeTotal = processingFee,
                    Variance = variance,
                    AutoMatched = autoConfirm,
                    RuleTriggered = ruleTriggered,
                    GatewayRecordCount = 1,
                    BankRecordCount = 1,
                    MatchedAt = now
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
                LinkedAt = now,
            });

            tenantDb.ReconciliationMatchedRecords.Add(new ReconciliationMatchedRecord
            {
                ReconciliationMatchedRecordId = Guid.NewGuid(),
                ReconciliationMatchGroupId = matchGroup.ReconciliationMatchGroupId,
                ImportedNormalizedRecordId = bankRecord.ImportedNormalizedRecordId,
                SourceType = "BANK",
                MatchAmount = bankRecord.NetAmount,
                LinkedAt = now,
            });

            // Claimed by this group either way — pending-review is still a claim, so a later cycle
            // (or another transaction's candidate search) doesn't propose the same BANK/GATEWAY
            // record again while this one awaits a human. RejectMatchAsync resets MatchStatus back
            // to PENDING if a reviewer decides this pairing was wrong.
            gatewayRecord.MatchStatus = MatchStatuses.Matched;
            bankRecord.MatchStatus = MatchStatuses.Matched;

            if (autoConfirm)
            {
                // Auto-confirmation must also promote the linked transaction — IsConfirmed alone
                // doesn't move it out of NeedsBankMatch; this is the same side effect
                // ReconciliationMatchConfirmationService runs for a human confirmation.
                await CardCashoutPromoter.PromoteLinkedTransactionAsync(tenantDb, matchGroup, confirmedByUserId: null, now, ct);
            }
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
