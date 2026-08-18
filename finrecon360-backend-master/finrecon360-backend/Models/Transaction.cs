namespace finrecon360_backend.Models
{
    public class Transaction
    {
        public Guid TransactionId { get; set; }
        public decimal Amount { get; set; }
        public DateTime TransactionDate { get; set; }
        public string Description { get; set; } = string.Empty;
        public Guid? BankAccountId { get; set; }
        public TransactionType TransactionType { get; set; }
        public PaymentMethod PaymentMethod { get; set; }
        // Pending is the entry state; approvals move to JournalReady or NeedsBankMatch depending on payment path.
        public TransactionState TransactionState { get; set; } = TransactionState.Pending;
        public Guid? CreatedByUserId { get; set; }
        public DateTime? ApprovedAt { get; set; }
        public Guid? ApprovedByUserId { get; set; }
        public DateTime? RejectedAt { get; set; }
        public Guid? RejectedByUserId { get; set; }
        public string? RejectionReason { get; set; }

        /// <summary>
        /// The upstream reference for this payment — gateway reference or bank narrative.
        ///
        /// Restored after a merge dropped it from the model while leaving the column in every
        /// tenant database and the field on the create form, so entered references were silently
        /// discarded by the model binder. It is also the strongest correlation key the matcher has:
        /// without it, bank matching falls back to date plus amount, which cannot distinguish two
        /// payments of the same value on the same day.
        /// </summary>
        public string? ReferenceNumber { get; set; }

        public string? CardLast4 { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        public BankAccount? BankAccount { get; set; }
        // Append-only lifecycle trail used for approval review and audit evidence.
        public ICollection<TransactionStateHistory> StateHistories { get; set; } = new List<TransactionStateHistory>();
    }
}
