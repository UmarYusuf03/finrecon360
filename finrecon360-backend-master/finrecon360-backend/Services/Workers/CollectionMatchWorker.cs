using finrecon360_backend.Data;
using finrecon360_backend.Models;
using finrecon360_backend.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace finrecon360_backend.Services.Workers
{
    /// <summary>
    /// Implements Collection Match (Rule 5): Physical Card-In ↔ Bank Statement.
    ///
    /// Match Key: Reference Number + Last 4 Card Digits
    ///
    /// Purpose: Confirms that physical card swipes at the business location (e.g. a POS
    /// terminal or manual card entry by staff) have actually cleared the bank account.
    /// A Card-In with no bank deposit means the card payment may have been declined
    /// post-approval or the bank has not yet settled.
    ///
    /// Source A (Internal): Transactions (CashIn + Card) in JournalReady state with CardLast4 set
    /// Source B (External): ImportedNormalizedRecords from committed BANK batches, scoped to the
    ///   transaction's BankAccountId when both the transaction and the BANK batch specify one
    ///   (card transactions always carry a BankAccountId per the
    ///   CK_Transactions_PaymentMethod_BankAccount check constraint). Untagged/legacy BANK
    ///   batches remain eligible for any transaction.
    ///
    /// Match logic:
    ///   Primary key  : ReferenceNumber (case-insensitive) + CardLast4 appears in BANK Description/AccountCode
    ///   Fallback key : Amount ± tolerance + Date ± tolerance days when reference is missing
    ///
    /// On exact match → ReconciliationMatchGroup(Level5, IsConfirmed=true)
    /// On variance    → ReconciliationEvent(Variance)
    /// On ambiguous match (2+ candidates satisfy the same key) → ReconciliationEvent(RequiresReview)
    /// On no match    → ReconciliationEvent(MatchNotFound)
    /// </summary>
    public class CollectionMatchWorker
    {
        private readonly ILogger<CollectionMatchWorker> _logger;
        private readonly IReconciliationSettingsProvider _settingsProvider;

        public CollectionMatchWorker(ILogger<CollectionMatchWorker> logger, IReconciliationSettingsProvider settingsProvider)
        {
            _logger = logger;
            _settingsProvider = settingsProvider;
        }

        public async Task<MatchingRunResult> ExecuteAsync(
            Guid tenantId,
            TenantDbContext tenantDb,
            CancellationToken ct = default)
        {
            var settings = await _settingsProvider.GetAsync(tenantDb, ct);
            var amountTolerance = settings.AmountTolerance;
            var dateToleranceDays = settings.DateToleranceDays;

            // 1. Load all JournalReady Card CashIn transactions (physical swipes) not yet Level5-matched.
            var cardInTransactions = await tenantDb.Transactions
                .Where(t =>
                    t.TransactionState == TransactionState.JournalReady &&
                    t.TransactionType == TransactionType.CashIn &&
                    t.PaymentMethod == PaymentMethod.Card)
                .ToListAsync(ct);

            if (cardInTransactions.Count == 0)
            {
                return MatchingRunResult.Empty(MatchLevels.Level5);
            }

            // 2. Load COMMITTED BANK records that are PENDING a match, with their batch's
            //    BankAccountId for account-scoped matching.
            var bankRecordsWithAccount = await (
                from r in tenantDb.ImportedNormalizedRecords
                join b in tenantDb.ImportBatches on r.ImportBatchId equals b.ImportBatchId
                where b.SourceType == "BANK" && b.Status == "COMMITTED" && r.MatchStatus == MatchStatuses.Pending
                select new { Record = r, BatchBankAccountId = b.BankAccountId }
            ).ToListAsync(ct);

            _logger.LogInformation(
                "CollectionMatchWorker: tenant {TenantId} — {CardInCount} Card-In txns, {BankCount} BANK records",
                tenantId, cardInTransactions.Count, bankRecordsWithAccount.Count);

            // 3. Build BANK lookup:
            //    Primary   — by "REF" when BANK record has a reference number.
            //    Secondary — by amount bucket (rounded to 2dp) + date window for fallback matching.
            var bankByRefLast4 = new Dictionary<string, List<(ImportedNormalizedRecord Record, Guid? BatchBankAccountId)>>(StringComparer.OrdinalIgnoreCase);
            var bankByAmountDate = new Dictionary<string, List<(ImportedNormalizedRecord Record, Guid? BatchBankAccountId)>>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in bankRecordsWithAccount)
            {
                var br = entry.Record;

                // Build amount+date key for fallback.
                var amountKey = $"{br.NetAmount:F2}|{br.TransactionDate:yyyy-MM-dd}";
                if (!bankByAmountDate.TryGetValue(amountKey, out var adList))
                {
                    adList = new List<(ImportedNormalizedRecord, Guid?)>();
                    bankByAmountDate[amountKey] = adList;
                }
                adList.Add((br, entry.BatchBankAccountId));

                // Build ref key if the BANK record has a reference.
                if (!string.IsNullOrWhiteSpace(br.ReferenceNumber))
                {
                    var refKey = br.ReferenceNumber.Trim().ToUpperInvariant();
                    if (!bankByRefLast4.TryGetValue(refKey, out var rList))
                    {
                        rList = new List<(ImportedNormalizedRecord, Guid?)>();
                        bankByRefLast4[refKey] = rList;
                    }
                    rList.Add((br, entry.BatchBankAccountId));
                }
            }

            var autoMatched = 0;
            var exceptions = 0;
            var noMatch = 0;
            var now = DateTime.UtcNow;

            foreach (var txn in cardInTransactions)
            {
                ImportedNormalizedRecord? matchedBankRecord = null;
                bool isExact = false;
                bool ambiguous = false;
                var ambiguousCount = 0;

                // A batch with no BankAccountId set (legacy/untagged import) stays eligible for
                // any transaction; otherwise it must match the transaction's account.
                bool AccountScoped((ImportedNormalizedRecord Record, Guid? BatchBankAccountId) x) =>
                    txn.BankAccountId == null || x.BatchBankAccountId == null || x.BatchBankAccountId == txn.BankAccountId;

                // --- Primary match: Reference Number ---
                if (!string.IsNullOrWhiteSpace(txn.Description))
                {
                    var refKey = txn.Description.Trim().ToUpperInvariant();
                    if (bankByRefLast4.TryGetValue(refKey, out var refCandidates))
                    {
                        // Further narrow by account, amount + card last4 in bank description (if available).
                        var matches = refCandidates
                            .Where(AccountScoped)
                            .Where(x =>
                                Math.Abs(x.Record.NetAmount - txn.Amount) < amountTolerance &&
                                (string.IsNullOrWhiteSpace(txn.CardLast4) ||
                                 string.IsNullOrWhiteSpace(x.Record.Description) ||
                                 x.Record.Description.Contains(txn.CardLast4!, StringComparison.OrdinalIgnoreCase)))
                            .Select(x => x.Record)
                            .ToList();

                        if (matches.Count == 1)
                        {
                            matchedBankRecord = matches[0];
                            isExact = true;
                        }
                        else if (matches.Count > 1)
                        {
                            ambiguous = true;
                            ambiguousCount = matches.Count;
                        }
                        else if (refCandidates.Any(AccountScoped))
                        {
                            // Ref matched but amount variance.
                            var best = refCandidates.First(AccountScoped).Record;
                            tenantDb.ReconciliationEvents.Add(new ReconciliationEvent
                            {
                                ReconciliationEventId = Guid.NewGuid(),
                                EventType = ReconciliationEventTypes.Variance,
                                MatchLevel = MatchLevels.Level5,
                                Details = $"Card-In txn {txn.TransactionId}: ref matches BANK record but " +
                                          $"amount differs (card={txn.Amount}, bank={best.NetAmount}, " +
                                          $"delta={Math.Abs(best.NetAmount - txn.Amount):F2}).",
                                CreatedAt = now,
                            });
                            exceptions++;
                            continue;
                        }
                    }
                }

                // --- Fallback: Amount + Date window ---
                if (matchedBankRecord == null && !ambiguous)
                {
                    for (var dayOffset = -dateToleranceDays; dayOffset <= dateToleranceDays; dayOffset++)
                    {
                        var lookupDate = txn.TransactionDate.AddDays(dayOffset);
                        var amountKey = $"{txn.Amount:F2}|{lookupDate:yyyy-MM-dd}";
                        if (bankByAmountDate.TryGetValue(amountKey, out var candidates))
                        {
                            // Further filter by account + CardLast4 in bank description when available.
                            var matches = candidates
                                .Where(AccountScoped)
                                .Where(x =>
                                    string.IsNullOrWhiteSpace(txn.CardLast4) ||
                                    string.IsNullOrWhiteSpace(x.Record.Description) ||
                                    x.Record.Description.Contains(txn.CardLast4!, StringComparison.OrdinalIgnoreCase))
                                .Select(x => x.Record)
                                .ToList();

                            if (matches.Count == 1)
                            {
                                matchedBankRecord = matches[0];
                                isExact = dayOffset == 0; // exact only if same calendar day
                                break;
                            }

                            if (matches.Count > 1)
                            {
                                ambiguous = true;
                                ambiguousCount = matches.Count;
                                break;
                            }
                        }
                    }
                }

                if (ambiguous)
                {
                    tenantDb.ReconciliationEvents.Add(new ReconciliationEvent
                    {
                        ReconciliationEventId = Guid.NewGuid(),
                        EventType = ReconciliationEventTypes.RequiresReview,
                        MatchLevel = MatchLevels.Level5,
                        Details = $"Ambiguous match: {ambiguousCount} BANK records satisfy the same match key " +
                                  $"for Card-In txn {txn.TransactionId} (amount={txn.Amount}).",
                        CreatedAt = now,
                    });
                    exceptions++;
                    continue;
                }

                if (matchedBankRecord == null)
                {
                    tenantDb.ReconciliationEvents.Add(new ReconciliationEvent
                    {
                        ReconciliationEventId = Guid.NewGuid(),
                        EventType = ReconciliationEventTypes.MatchNotFound,
                        MatchLevel = MatchLevels.Level5,
                        Details = $"Card-In txn {txn.TransactionId} (amount={txn.Amount}, " +
                                  $"date={txn.TransactionDate:yyyy-MM-dd}, last4={txn.CardLast4 ?? "n/a"}) " +
                                  $"has no matching BANK record. Card payment may be pending settlement.",
                        CreatedAt = now,
                    });
                    noMatch++;
                    continue;
                }

                // Create Level5 match group.
                var settlementKey = $"CARDIN_BANK|{txn.TransactionId}";
                var matchGroup = new ReconciliationMatchGroup
                {
                    ReconciliationMatchGroupId = Guid.NewGuid(),
                    MatchLevel = MatchLevels.Level5,
                    SettlementKey = settlementKey,
                    IsConfirmed = isExact,          // Non-exact (date drift) goes to pending review.
                    ConfirmedAt = isExact ? now : null,
                    MatchedAmount = txn.Amount,
                    Variance = Math.Abs(matchedBankRecord.NetAmount - txn.Amount),
                    Status = isExact ? "Confirmed" : "Pending",
                    CreatedAt = now,
                };
                tenantDb.ReconciliationMatchGroups.Add(matchGroup);

                tenantDb.ReconciliationMatchedRecords.Add(new ReconciliationMatchedRecord
                {
                    ReconciliationMatchedRecordId = Guid.NewGuid(),
                    ReconciliationMatchGroupId = matchGroup.ReconciliationMatchGroupId,
                    ImportedNormalizedRecordId = matchedBankRecord.ImportedNormalizedRecordId,
                    SourceType = "BANK",
                    LinkedAt = now,
                });

                matchedBankRecord.MatchStatus = MatchStatuses.Matched;
                matchedBankRecord.SettlementKey = settlementKey;

                _logger.LogInformation(
                    "Level5 {Status}: Card-In {TxnId} ↔ BANK {BankId} (exact={Exact})",
                    isExact ? "AUTO-MATCHED" : "PENDING-REVIEW",
                    txn.TransactionId, matchedBankRecord.ImportedNormalizedRecordId, isExact);

                autoMatched++;
            }

            await tenantDb.SaveChangesAsync(ct);

            _logger.LogInformation(
                "CollectionMatchWorker done — matched={M}, exceptions={E}, noMatch={N}",
                autoMatched, exceptions, noMatch);

            return new MatchingRunResult(MatchLevels.Level5, cardInTransactions.Count, autoMatched, exceptions, noMatch);
        }
    }
}
