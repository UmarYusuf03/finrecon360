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

        /// <summary>
        /// The upstream reference for this payment — a gateway reference or bank narrative.
        /// Optional, because a manually recorded cash transaction has no upstream document, but it
        /// is the strongest key available for correlating this record with an imported one later.
        /// </summary>
        [MaxLength(100)]
        public string? ReferenceNumber { get; set; }

        public Guid? BankAccountId { get; set; }

        [Required]
        public string TransactionType { get; set; } = string.Empty;

        [Required]
        public string PaymentMethod { get; set; } = string.Empty;

        /// <summary>
        /// Last four digits of the card, for card payments only. Not sensitive on its own — it
        /// cannot reconstruct a card number — and it is what lets a person confirm that a bank
        /// line and a recorded cash-out refer to the same card.
        /// </summary>
        [MaxLength(4)]
        public string? CardLast4 { get; set; }
    }

    public class UpdateTransactionRequest
    {
        public decimal Amount { get; set; }
        public DateTime TransactionDate { get; set; }

        [Required]
        [MaxLength(500)]
        public string Description { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? ReferenceNumber { get; set; }

        public Guid? BankAccountId { get; set; }

        [Required]
        public string TransactionType { get; set; } = string.Empty;

        [Required]
        public string PaymentMethod { get; set; } = string.Empty;

        /// <summary>
        /// Last four digits of the card, for card payments only. Not sensitive on its own — it
        /// cannot reconstruct a card number — and it is what lets a person confirm that a bank
        /// line and a recorded cash-out refer to the same card.
        /// </summary>
        [MaxLength(4)]
        public string? CardLast4 { get; set; }
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
        string? ReferenceNumber,
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
        string? CardLast4,
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
