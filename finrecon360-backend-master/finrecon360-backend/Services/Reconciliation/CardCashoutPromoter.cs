using finrecon360_backend.Data;
using finrecon360_backend.Models;
using Microsoft.EntityFrameworkCore;

namespace finrecon360_backend.Services.Reconciliation
{
    /// <summary>
    /// Promotes the card-cashout Transaction linked to a confirmed Level4 match group from
    /// NeedsBankMatch to JournalReady. Shared by two callers that both need this same side
    /// effect on confirmation:
    ///   - BankStatementReconciliationWorker, immediately after auto-confirming a Tier1/Tier2 match
    ///   - ReconciliationMatchConfirmationService, when a human confirms a Tier3 (or legacy
    ///     always-manual) match
    /// Stages changes on the tracked context but does not call SaveChangesAsync — the caller's
    /// existing unit of work owns that.
    /// </summary>
    public static class CardCashoutPromoter
    {
        /// <param name="confirmedByUserId">
        /// Null for a system (auto-confirm) promotion; set to the confirming user's id for a
        /// human confirmation — recorded on the state-history row either way.
        /// </param>
        public static async Task PromoteLinkedTransactionAsync(
            TenantDbContext db,
            ReconciliationMatchGroup group,
            Guid? confirmedByUserId,
            DateTime now,
            CancellationToken ct = default)
        {
            // Prefer the TransactionId already recorded in the group's metadata — direct lookup,
            // not amount-proximity guessing. This matters for Tier2 (fee-explained) matches:
            // group.MatchedAmount is the BANK side's net amount, which is deliberately different
            // from txn.Amount by the processing fee, so an amount-based search would silently
            // fail to find the transaction for exactly the matches Tier2 exists to handle.
            var metadata = MatchGroupMetadata.TryParse(group.MatchMetadataJson);

            var txn = metadata?.TransactionId is { } transactionId
                ? await db.Transactions.FirstOrDefaultAsync(t =>
                    t.TransactionId == transactionId &&
                    t.TransactionState == TransactionState.NeedsBankMatch, ct)
                : await db.Transactions
                    .Where(t =>
                        t.TransactionState == TransactionState.NeedsBankMatch &&
                        t.TransactionType == TransactionType.CashOut &&
                        t.PaymentMethod == PaymentMethod.Card &&
                        Math.Abs(t.Amount - group.MatchedAmount) < 0.01m)
                    .FirstOrDefaultAsync(ct);

            if (txn == null)
            {
                return;
            }

            var fromState = txn.TransactionState;
            txn.TransactionState = TransactionState.JournalReady;
            txn.UpdatedAt = now;

            db.TransactionStateHistories.Add(new TransactionStateHistory
            {
                TransactionStateHistoryId = Guid.NewGuid(),
                TransactionId = txn.TransactionId,
                FromState = fromState,
                ToState = TransactionState.JournalReady,
                ChangedByUserId = confirmedByUserId,
                ChangedAt = now,
                Note = confirmedByUserId.HasValue
                    ? $"Promoted to JournalReady by match group {group.ReconciliationMatchGroupId} confirmation"
                    : $"Auto-promoted to JournalReady by match group {group.ReconciliationMatchGroupId} (Level4 auto-confirm)",
            });
        }
    }
}
