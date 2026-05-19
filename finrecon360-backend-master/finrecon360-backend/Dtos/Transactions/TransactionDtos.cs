using System.ComponentModel.DataAnnotations;

namespace finrecon360_backend.Dtos.Transactions
{
    public class CreateTransactionRequest
    {
        public decimal Amount { get; set; }
        public DateTime TransactionDate { get; set; }

        [MaxLength(100)]
        public string? ReferenceNumber { get; set; }

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

        [MaxLength(100)]
        public string? ReferenceNumber { get; set; }

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
        string? ReferenceNumber,
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

    /// <summary>
    /// WHY: The NeedsBankMatch queue is the handoff point between the transaction approval workflow
    /// and the reconciliation engine. Accountants need to see the gateway import evidence (gross, fee, net,
    /// settlement ID) alongside the transaction so they can understand what the payment gateway
    /// reported before the bank statement is imported and Level-4 matching runs.
    /// </summary>
    public record NeedsBankMatchResponse(
        // ── Core transaction fields ───────────────────────────────────────────
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
        DateTime CreatedAt,
        // ── Matched import record context (nullable — may not yet be imported) ─
        Guid? ImportedNormalizedRecordId,
        string? ImportSourceType,
        string? ReferenceNumber,
        string? AccountCode,
        decimal? GrossAmount,
        decimal? ProcessingFee,
        decimal NetImportAmount,
        string? SettlementId,
        string MatchStatus,
        // ── Reconciliation match group context (nullable — may not yet be matched) ─
        Guid? ReconciliationMatchGroupId,
        string? MatchLevel,
        bool IsConfirmed,
        bool IsJournalPosted,
        string? MatchMetadataJson);
}
