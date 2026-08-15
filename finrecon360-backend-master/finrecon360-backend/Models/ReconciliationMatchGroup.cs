namespace finrecon360_backend.Models
{
    /// <summary>
    /// A reconciliation match group links two or more imported records that have been
    /// determined to correspond to the same underlying financial event across two different
    /// source systems (e.g. GATEWAY payout matched against a BANK deposit).
    ///
    /// MatchLevel indicates which reconciliation rule produced the group:
    ///   Level1 = Operational Match  (Staff Manual Input ↔ POS EOD)
    ///   Level2 = Sync Audit         (POS EOD ↔ ERP Sales Ledger)
    ///   Level3 = Sales Match        (ERP Sales Ledger ↔ Payment Gateway)
    ///   Level4 = Expense Match      (Approved Card Cashout ↔ Bank Statement)
    ///   Level5 = Collection Match   (Physical Card-In ↔ Bank Statement)
    ///   Level6 = Settlement Match   (Gateway Payout Total ↔ Bank Statement)
    /// </summary>
    public class ReconciliationMatchGroup
    {
        public Guid ReconciliationMatchGroupId { get; set; }

        // Which rule produced this group (Level1..Level6).
        public string MatchLevel { get; set; } = string.Empty;

        // Composite key used to identify the matched event (e.g. "ACCT001|REF001" or SettlementId).
        public string SettlementKey { get; set; } = string.Empty;

        // When true, a human reviewer has confirmed the match is correct.
        // Auto-confirmed for exact-match scenarios; set to false for variance/ambiguous matches.
        public bool IsConfirmed { get; set; }

        public Guid? ConfirmedByUserId { get; set; }
        public DateTime? ConfirmedAt { get; set; }

        // The net amount that was matched (typically the transaction amount).
        public decimal MatchedAmount { get; set; }

        // Difference between Source A and Source B amounts (0 for exact matches).
        public decimal Variance { get; set; }

        // Pending | Confirmed | Rejected
        public string Status { get; set; } = "Pending";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation — all imported records that are part of this group.
        public ICollection<ReconciliationMatchedRecord> MatchedRecords { get; set; } =
            new List<ReconciliationMatchedRecord>();
    }
}
