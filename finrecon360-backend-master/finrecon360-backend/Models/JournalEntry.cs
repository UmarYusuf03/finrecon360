namespace finrecon360_backend.Models
{
    /// <summary>
    /// Represents a posted journal entry in the tenant's general ledger.
    /// Journal entries are downstream effects of completed reconciliation workflows.
    /// Cash cashouts post after approval; card cashouts post only after bank match confirmation.
    /// Gateway fee adjustments post automatically on Level-4 MATCHED confirmation.
    /// </summary>
    public class JournalEntry
    {
        public Guid JournalEntryId { get; set; }

        /// <summary>
        /// Links to the Transaction that triggered this journal entry (cashout path).
        /// Null for gateway fee-adjustment entries that originate from reconciliation groups.
        /// </summary>
        public Guid? TransactionId { get; set; }

        /// <summary>
        /// Links to the ReconciliationMatchGroup that triggered this entry (gateway settlement path).
        /// Populated for fee-adjustment entries generated after Level-4 bank confirmation.
        /// </summary>
        public Guid? ReconciliationMatchGroupId { get; set; }

        /// <summary>
        /// The voucher this entry belongs to. All entries in a voucher must sum to zero —
        /// enforced by JournalPostingExecutorWorker/ReconciliationController before posting,
        /// not by a database constraint (SQL Server can't easily check a cross-row aggregate).
        /// </summary>
        public Guid? JournalVoucherId { get; set; }

        /// <summary>
        /// The GL account this entry posts against. Nullable so pre-existing entries and any
        /// entry type without a seeded chart-of-accounts mapping don't fail to save.
        /// </summary>
        public Guid? ChartOfAccountId { get; set; }

        /// <summary>
        /// Type of journal entry:
        /// 'Revenue' | 'Expense' | 'FeeAdjustment' | 'CashOut' | 'CashIn' |
        /// 'DebitBank' | 'CreditCashOut' | 'DebitFeeExpense' | 'CreditFeeOffset'
        /// </summary>
        public string EntryType { get; set; } = null!;

        public decimal Amount { get; set; }
        public string Currency { get; set; } = "LKR";

        /// <summary>
        /// UTC timestamp when the journal entry was posted. Immutable after creation.
        /// </summary>
        public DateTime PostedAt { get; set; }

        public Guid? PostedByUserId { get; set; }

        public string? Notes { get; set; }

        // Navigation
        public Transaction? Transaction { get; set; }
        public ReconciliationMatchGroup? ReconciliationMatchGroup { get; set; }
        public JournalVoucher? JournalVoucher { get; set; }
        public ChartOfAccount? ChartOfAccount { get; set; }
    }
}
