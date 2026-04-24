using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace finrecon360_backend.Models
{
    /// <summary>
    /// Tracks a single bank statement file import batch.
    /// </summary>
    public class BankStatementImport
    {
        [Key]
        public Guid Id { get; set; }

        public Guid BatchId { get; set; }

        public Guid BankAccountId { get; set; }

        [Required]
        [MaxLength(260)]
        public string FileName { get; set; } = string.Empty;

        public DateTime ImportDate { get; set; }

        public BankStatementImportStatus Status { get; set; } = BankStatementImportStatus.Pending;

        public int TotalRows { get; set; }

        public int ValidRows { get; set; }

        public Guid TenantId { get; set; }

        // Audit fields
        public DateTime CreatedAt { get; set; }
        public Guid? CreatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public Guid? UpdatedBy { get; set; }

        public ICollection<BankStatementLine> BankStatementLines { get; set; } = new List<BankStatementLine>();
    }

    /// <summary>
    /// Represents one parsed transaction row from a bank statement.
    /// </summary>
    public class BankStatementLine
    {
        [Key]
        public Guid Id { get; set; }

        [ForeignKey(nameof(BankStatementImport))]
        public Guid BankStatementImportId { get; set; }

        public DateTime TransactionDate { get; set; }

        public DateTime? PostingDate { get; set; }

        [MaxLength(100)]
        public string? ReferenceNumber { get; set; }

        [MaxLength(500)]
        public string? Description { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; }

        public bool IsReconciled { get; set; }

        public Guid TenantId { get; set; }

        // Audit fields
        public DateTime CreatedAt { get; set; }
        public Guid? CreatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public Guid? UpdatedBy { get; set; }

        public BankStatementImport BankStatementImport { get; set; } = default!;
        public ICollection<ReconciliationException> Exceptions { get; set; } = new List<ReconciliationException>();
    }

    /// <summary>
    /// Represents a reconciliation session for a specific bank account.
    /// </summary>
    public class ReconciliationRun
    {
        [Key]
        public Guid Id { get; set; }

        public DateTime RunDate { get; set; }

        public Guid BankAccountId { get; set; }

        public ReconciliationRunStatus Status { get; set; } = ReconciliationRunStatus.InProgress;

        public int TotalMatchesProposed { get; set; }

        public Guid TenantId { get; set; }

        // Audit fields
        public DateTime CreatedAt { get; set; }
        public Guid? CreatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public Guid? UpdatedBy { get; set; }

        public ICollection<MatchGroup> MatchGroups { get; set; } = new List<MatchGroup>();
        public ICollection<ReconciliationException> Exceptions { get; set; } = new List<ReconciliationException>();
    }

    /// <summary>
    /// Groups one-to-many or many-to-one candidate matches.
    /// </summary>
    public class MatchGroup
    {
        [Key]
        public Guid Id { get; set; }

        [ForeignKey(nameof(ReconciliationRun))]
        public Guid ReconciliationRunId { get; set; }

        [Column(TypeName = "decimal(5,4)")]
        public decimal? MatchConfidenceScore { get; set; }

        public MatchGroupStatus Status { get; set; } = MatchGroupStatus.Proposed;

        public Guid TenantId { get; set; }

        // Audit fields
        public DateTime CreatedAt { get; set; }
        public Guid? CreatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public Guid? UpdatedBy { get; set; }

        public ReconciliationRun ReconciliationRun { get; set; } = default!;
        public ICollection<MatchDecision> MatchDecisions { get; set; } = new List<MatchDecision>();
    }

    /// <summary>
    /// Stores human decisions on proposed match groups for full auditability.
    /// </summary>
    public class MatchDecision
    {
        [Key]
        public Guid Id { get; set; }

        [ForeignKey(nameof(MatchGroup))]
        public Guid MatchGroupId { get; set; }

        public MatchDecisionStatus Decision { get; set; }

        [MaxLength(1000)]
        public string? DecisionReason { get; set; }

        public Guid DecidedBy { get; set; }

        public DateTime DecidedAt { get; set; }

        public Guid TenantId { get; set; }

        // Audit fields
        public DateTime CreatedAt { get; set; }
        public Guid? CreatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public Guid? UpdatedBy { get; set; }

        public MatchGroup MatchGroup { get; set; } = default!;
    }

    /// <summary>
    /// Represents unmatched or problematic items requiring manual investigation.
    /// Uses a distinct class name to avoid collision with System.Exception.
    /// </summary>
    [Table("Exception")]
    public class ReconciliationException
    {
        [Key]
        public Guid Id { get; set; }

        [ForeignKey(nameof(ReconciliationRun))]
        public Guid ReconciliationRunId { get; set; }

        [ForeignKey(nameof(BankStatementLine))]
        public Guid? BankStatementLineId { get; set; }

        // Placeholder: canonical transaction aggregate ID from accounting side.
        public Guid? CanonicalTransactionId { get; set; }

        [Required]
        [MaxLength(1000)]
        public string Reason { get; set; } = string.Empty;

        public ReconciliationExceptionStatus Status { get; set; } = ReconciliationExceptionStatus.Open;

        public Guid TenantId { get; set; }

        // Audit fields
        public DateTime CreatedAt { get; set; }
        public Guid? CreatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public Guid? UpdatedBy { get; set; }

        public ReconciliationRun ReconciliationRun { get; set; } = default!;
        public BankStatementLine? BankStatementLine { get; set; }
    }

    /// <summary>
    /// Denormalized KPI snapshot for dashboard/reporting jobs.
    /// </summary>
    public class ReportSnapshot
    {
        [Key]
        public Guid Id { get; set; }

        public DateTime SnapshotDate { get; set; }

        public int TotalUnmatchedCashouts { get; set; }

        public int PendingCardMatches { get; set; }

        [Column(TypeName = "decimal(5,2)")]
        public decimal ReconciliationCompletionPercentage { get; set; }

        public Guid TenantId { get; set; }

        // Audit fields
        public DateTime CreatedAt { get; set; }
        public Guid? CreatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public Guid? UpdatedBy { get; set; }
    }

    public enum BankStatementImportStatus
    {
        Pending = 0,
        Parsed = 1,
        Failed = 2
    }

    public enum ReconciliationRunStatus
    {
        InProgress = 0,
        Completed = 1,
        NeedsReview = 2
    }

    public enum MatchGroupStatus
    {
        Proposed = 0,
        Confirmed = 1,
        Rejected = 2
    }

    public enum MatchDecisionStatus
    {
        Approved = 0,
        Rejected = 1
    }

    public enum ReconciliationExceptionStatus
    {
        Open = 0,
        Investigating = 1,
        Resolved = 2
    }
}