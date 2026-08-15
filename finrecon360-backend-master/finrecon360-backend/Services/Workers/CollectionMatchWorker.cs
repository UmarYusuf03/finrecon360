using finrecon360_backend.Data;
using finrecon360_backend.Models;
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
    /// Source B (External): ImportedNormalizedRecords from committed BANK batches
    ///
    /// Match logic:
    ///   Primary key  : ReferenceNumber (case-insensitive) + CardLast4 appears in BANK Description/AccountCode
    ///   Fallback key : Amount ± tolerance + Date ± 1 day when reference is missing
    ///
    /// On exact match → ReconciliationMatchGroup(Level5, IsConfirmed=true)
    /// On variance    → ReconciliationEvent(Variance)
    /// On no match    → ReconciliationEvent(MatchNotFound)
    /// </summary>
    public class CollectionMatchWorker
    {
        private readonly ILogger<CollectionMatchWorker> _logger;
        private const decimal AmountTolerance = 0.01m;
        private const int DateToleranceDays = 1;

        public CollectionMatchWorker(ILogger<CollectionMatchWorker> logger)
        {
            _logger = logger;
        }

        public async Task<MatchingRunResult> ExecuteAsync(
            Guid tenantId,
            TenantDbContext tenantDb,
            CancellationToken ct = default)
        {
            // 1. Load all JournalReady Card CashIn transactions (physical swipes) not yet Level5-matched.
            var cardInTransactions = await tenantDb.Transactions
                .Where(t =>
                    t.TransactionState == TransactionState.JournalReady &&
                    t.TransactionType == TransactionType.CashIn &&
                    t.PaymentMethod == PaymentMethod.Card)
                .ToListAsync(ct);

            if (cardInTransactions.Count == 0)
            {
                return MatchingRunResult.Empty("Level5");
            }

            // 2. Load COMMITTED BANK records that are PENDING a match.
            var bankRecords = await (
                from r in tenantDb.ImportedNormalizedRecords
                join b in tenantDb.ImportBatches on r.ImportBatchId equals b.ImportBatchId
                where b.SourceType == "BANK" && b.Status == "COMMITTED" && r.MatchStatus == "PENDING"
                select r
            ).ToListAsync(ct);

            _logger.LogInformation(
                "CollectionMatchWorker: tenant {TenantId} — {CardInCount} Card-In txns, {BankCount} BANK records",
                tenantId, cardInTransactions.Count, bankRecords.Count);

            // 3. Build BANK lookup:
            //    Primary   — by "REF|LAST4" when BANK record has reference + last 4 in description/account.
            //    Secondary — by amount bucket (rounded to 2dp) + date window for fallback matching.
            var bankByRefLast4 = new Dictionary<string, List<ImportedNormalizedRecord>>(StringComparer.OrdinalIgnoreCase);
            var bankByAmountDate = new Dictionary<string, List<ImportedNormalizedRecord>>(StringComparer.OrdinalIgnoreCase);

            foreach (var br in bankRecords)
            {
                // Build amount+date key for fallback.
                var amountKey = $"{br.NetAmount:F2}|{br.TransactionDate:yyyy-MM-dd}";
                if (!bankByAmountDate.TryGetValue(amountKey, out var adList))
                {
                    adList = new List<ImportedNormalizedRecord>();
                    bankByAmountDate[amountKey] = adList;
                }
                adList.Add(br);

                // Build ref+last4 key if the BANK record has reference and description containing card digits.
                if (!string.IsNullOrWhiteSpace(br.ReferenceNumber))
                {
                    var refKey = br.ReferenceNumber.Trim().ToUpperInvariant();
                    if (!bankByRefLast4.TryGetValue(refKey, out var rList))
                    {
                        rList = new List<ImportedNormalizedRecord>();
                        bankByRefLast4[refKey] = rList;
                    }
                    rList.Add(br);
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

                // --- Primary match: Reference Number ---
                if (!string.IsNullOrWhiteSpace(txn.Description))
                {
                    var refKey = txn.Description.Trim().ToUpperInvariant();
                    if (bankByRefLast4.TryGetValue(refKey, out var refCandidates))
                    {
                        // Further narrow by amount + card last4 in bank description (if available).
                        matchedBankRecord = refCandidates.FirstOrDefault(b =>
                            Math.Abs(b.NetAmount - txn.Amount) < AmountTolerance &&
                            (string.IsNullOrWhiteSpace(txn.CardLast4) ||
                             string.IsNullOrWhiteSpace(b.Description) ||
                             b.Description.Contains(txn.CardLast4!, StringComparison.OrdinalIgnoreCase)));

                        if (matchedBankRecord != null)
                        {
                            isExact = true;
                        }
                        else if (refCandidates.Any())
                        {
                            // Ref matched but amount variance.
                            var best = refCandidates[0];
                            tenantDb.ReconciliationEvents.Add(new ReconciliationEvent
                            {
                                ReconciliationEventId = Guid.NewGuid(),
                                EventType = "Variance",
                                MatchLevel = "Level5",
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

                // --- Fallback: Amount + Date window (±1 day) ---
                if (matchedBankRecord == null)
                {
                    for (var dayOffset = -DateToleranceDays; dayOffset <= DateToleranceDays; dayOffset++)
                    {
                        var lookupDate = txn.TransactionDate.AddDays(dayOffset);
                        var amountKey = $"{txn.Amount:F2}|{lookupDate:yyyy-MM-dd}";
                        if (bankByAmountDate.TryGetValue(amountKey, out var candidates))
                        {
                            // Further filter by CardLast4 in bank description when available.
                            matchedBankRecord = candidates.FirstOrDefault(b =>
                                string.IsNullOrWhiteSpace(txn.CardLast4) ||
                                string.IsNullOrWhiteSpace(b.Description) ||
                                b.Description.Contains(txn.CardLast4!, StringComparison.OrdinalIgnoreCase));

                            if (matchedBankRecord != null)
                            {
                                isExact = dayOffset == 0; // exact only if same calendar day
                                break;
                            }
                        }
                    }
                }

                if (matchedBankRecord == null)
                {
                    tenantDb.ReconciliationEvents.Add(new ReconciliationEvent
                    {
                        ReconciliationEventId = Guid.NewGuid(),
                        EventType = "MatchNotFound",
                        MatchLevel = "Level5",
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
                    MatchLevel = "Level5",
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

                matchedBankRecord.MatchStatus = "MATCHED";
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

            return new MatchingRunResult("Level5", cardInTransactions.Count, autoMatched, exceptions, noMatch);
        }
    }
}
