namespace finrecon360_backend.Models
{
    public class TransactionStateHistory
    {
        public Guid TransactionStateHistoryId { get; set; }
        public Guid TransactionId { get; set; }
        public TransactionState FromState { get; set; }
        public TransactionState ToState { get; set; }
        public Guid? ChangedByUserId { get; set; }
        public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
        public string? Note { get; set; }

        public Transaction Transaction { get; set; } = null!;
    }
}
