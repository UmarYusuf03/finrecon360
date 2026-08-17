namespace finrecon360_backend.Models
{
    public enum AccountType
    {
        Asset,
        Liability,
        Equity,
        Revenue,
        Expense,
    }

    /// <summary>
    /// A minimal general-ledger chart of accounts. JournalEntry rows reference one of these
    /// instead of only carrying a free-text EntryType string, so postings resolve to a real
    /// account rather than just a label.
    /// </summary>
    public class ChartOfAccount
    {
        public Guid ChartOfAccountId { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public AccountType AccountType { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
