namespace finrecon360_backend.Models
{
    /// <summary>
    /// Groups the JournalEntry rows produced by a single posting event (an automated
    /// double-entry cashout/fee posting, or a manual single-entry post) so the set can be
    /// verified to sum to zero and displayed together as one GL voucher, instead of each
    /// JournalEntry existing as an independent, ungrouped row.
    /// </summary>
    public class JournalVoucher
    {
        public Guid JournalVoucherId { get; set; }
        public Guid? TransactionId { get; set; }
        public Guid? ReconciliationMatchGroupId { get; set; }

        // Pending | Posted | Failed — Failed is used when entries didn't balance and posting
        // was rejected rather than committed with bad data.
        public string Status { get; set; } = "Posted";

        public DateTime PostedAt { get; set; } = DateTime.UtcNow;
        public Guid? PostedByUserId { get; set; }

        public ICollection<JournalEntry> Entries { get; set; } = new List<JournalEntry>();

        // Navigation — lets EF's change tracker fix up and order INSERTs against these FKs even
        // when the referenced row is new in the same SaveChanges batch (see PosSettlementPoster,
        // which stages a JournalVoucher and its ReconciliationMatchGroup together).
        public Transaction? Transaction { get; set; }
        public ReconciliationMatchGroup? ReconciliationMatchGroup { get; set; }
    }
}
