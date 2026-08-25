using finrecon360_backend.Data;
using finrecon360_backend.Models;
using Microsoft.EntityFrameworkCore;

namespace finrecon360_backend.Services.Reconciliation
{
    /// <summary>
    /// Posts the split GL entries for a confirmed Level7 (POS-terminal batch settlement) match
    /// group: DEBIT Bank (Net), DEBIT MDR Fee, CREDIT POS Clearing (Gross) — balances to zero by
    /// construction since Net = Gross - Fee. Shared by two callers that both need to post the
    /// same shape on confirmation:
    ///   - PosSettlementMatchWorker, immediately after auto-confirming a Tier1/2 match
    ///   - ReconciliationMatchConfirmationService, when a human confirms a Tier3 match
    /// Stages changes on the tracked context but does not call SaveChangesAsync — the caller's
    /// existing unit of work owns that.
    /// </summary>
    public static class PosSettlementPoster
    {
        private const string BankAccountCode = "1000-BANK";
        private const string FeeAccountCode = "5000-FEE";
        private const string PosClearingAccountCode = "6000-POSCLEARING";

        /// <returns>
        /// True if the voucher was staged. False if the group has no parseable metadata, is
        /// already posted, or the entries don't balance to zero — the caller should log and
        /// treat this as a failed posting rather than a match failure (the match itself already
        /// succeeded; only the GL step is being rejected).
        /// </returns>
        public static async Task<bool> PostAsync(TenantDbContext db, ReconciliationMatchGroup group, CancellationToken ct = default)
        {
            if (group.IsJournalPosted)
            {
                return false;
            }

            var metadata = MatchGroupMetadata.TryParse(group.MatchMetadataJson);
            if (metadata is null)
            {
                return false;
            }

            var accountsByCode = await db.ChartOfAccounts
                .AsNoTracking()
                .Where(a => a.Code == BankAccountCode || a.Code == FeeAccountCode || a.Code == PosClearingAccountCode)
                .ToDictionaryAsync(a => a.Code, a => (Guid?)a.ChartOfAccountId, ct);

            var voucher = new JournalVoucher
            {
                JournalVoucherId = Guid.NewGuid(),
                ReconciliationMatchGroupId = group.ReconciliationMatchGroupId,
                // Setting the navigation (not just the scalar FK) lets EF's change tracker fix up
                // and order this INSERT after the match group's even though group is new/unsaved
                // in this same SaveChanges call — see the relationship config in TenantDbContext.
                ReconciliationMatchGroup = group,
                Status = "Posted",
                PostedAt = DateTime.UtcNow,
            };

            var entries = new List<JournalEntry>
            {
                BuildEntry(voucher.JournalVoucherId, group.ReconciliationMatchGroupId, "DebitBankPos",
                    metadata.BankNetTotal, accountsByCode.GetValueOrDefault(BankAccountCode),
                    "POS settlement bank deposit (net)"),
                BuildEntry(voucher.JournalVoucherId, group.ReconciliationMatchGroupId, "DebitMdrFee",
                    metadata.PosFeeTotal, accountsByCode.GetValueOrDefault(FeeAccountCode),
                    "MDR fee for POS settlement"),
                BuildEntry(voucher.JournalVoucherId, group.ReconciliationMatchGroupId, "CreditPosClearingGross",
                    -metadata.PosGrossTotal, accountsByCode.GetValueOrDefault(PosClearingAccountCode),
                    "Clears POS clearing liability (gross)"),
            };

            var total = entries.Sum(e => e.Amount);
            if (total != 0m)
            {
                return false;
            }

            db.JournalVouchers.Add(voucher);
            db.JournalEntries.AddRange(entries);
            group.IsJournalPosted = true;
            group.UpdatedAt = DateTime.UtcNow;

            return true;
        }

        private static JournalEntry BuildEntry(
            Guid voucherId, Guid groupId, string entryType, decimal amount, Guid? accountId, string notes) =>
            new()
            {
                JournalEntryId = Guid.NewGuid(),
                JournalVoucherId = voucherId,
                ReconciliationMatchGroupId = groupId,
                ChartOfAccountId = accountId,
                EntryType = entryType,
                Amount = amount,
                Currency = "LKR",
                PostedAt = DateTime.UtcNow,
                Notes = notes,
            };
    }
}
