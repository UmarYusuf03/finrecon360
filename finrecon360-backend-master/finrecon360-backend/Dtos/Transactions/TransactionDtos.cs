using System.ComponentModel.DataAnnotations;

namespace finrecon360_backend.Dtos.Transactions
{
    public class CreateTransactionRequest
    {
        public decimal Amount { get; set; }
        public DateTime TransactionDate { get; set; }

        [Required]
        [MaxLength(500)]
        public string Description { get; set; } = string.Empty;

        public Guid? BankAccountId { get; set; }

        [Required]
        public string TransactionType { get; set; } = string.Empty;

        [Required]
        public string PaymentMethod { get; set; } = string.Empty;
    }

    public class UpdateTransactionRequest
    {
        public decimal Amount { get; set; }
        public DateTime TransactionDate { get; set; }

        [Required]
        [MaxLength(500)]
        public string Description { get; set; } = string.Empty;

        public Guid? BankAccountId { get; set; }

        [Required]
        public string TransactionType { get; set; } = string.Empty;

        [Required]
        public string PaymentMethod { get; set; } = string.Empty;
    }

    public class ApproveTransactionRequest
    {
        [MaxLength(500)]
        public string? Note { get; set; }
    }

    public class RejectTransactionRequest
    {
        [Required]
        [MaxLength(500)]
        public string Reason { get; set; } = string.Empty;
    }

    public record TransactionResponse(
        Guid TransactionId,
        decimal Amount,
        DateTime TransactionDate,
        string Description,
        Guid? BankAccountId,
        string TransactionType,
        string PaymentMethod,
        string TransactionState,
        Guid? CreatedByUserId,
        DateTime? ApprovedAt,
        Guid? ApprovedByUserId,
        DateTime? RejectedAt,
        Guid? RejectedByUserId,
        string? RejectionReason,
        DateTime CreatedAt,
        DateTime? UpdatedAt);

    public record TransactionStateHistoryResponse(
        Guid TransactionStateHistoryId,
        Guid TransactionId,
        string FromState,
        string ToState,
        Guid? ChangedByUserId,
        DateTime ChangedAt,
        string? Note);
}
