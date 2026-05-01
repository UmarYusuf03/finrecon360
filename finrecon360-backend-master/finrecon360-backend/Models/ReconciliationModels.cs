using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace finrecon360_backend.Models
{
    public class ReconciliationRun
    {
        [Key]
        public Guid ReconciliationRunId { get; set; }

        public DateTime RunDate { get; set; }

        public Guid BankAccountId { get; set; }

        public ReconciliationRunStatus Status { get; set; } = ReconciliationRunStatus.InProgress;

        public int TotalMatchesProposed { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Guid? CreatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public Guid? UpdatedBy { get; set; }

        public ICollection<MatchGroup> MatchGroups { get; set; } = new List<MatchGroup>();
        public ICollection<ReconciliationException> Exceptions { get; set; } = new List<ReconciliationException>();
    }

    public class MatchGroup
    {
        [Key]
        public Guid MatchGroupId { get; set; }

        [ForeignKey(nameof(ReconciliationRun))]
        public Guid ReconciliationRunId { get; set; }

        [Column(TypeName = "decimal(5,4)")]
        public decimal? MatchConfidenceScore { get; set; }

        public MatchGroupStatus Status { get; set; } = MatchGroupStatus.Proposed;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Guid? CreatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public Guid? UpdatedBy { get; set; }

        public ReconciliationRun ReconciliationRun { get; set; } = default!;
        public ICollection<MatchDecision> MatchDecisions { get; set; } = new List<MatchDecision>();
    }

    public class MatchDecision
    {
        [Key]
        public Guid MatchDecisionId { get; set; }

        [ForeignKey(nameof(MatchGroup))]
        public Guid MatchGroupId { get; set; }

        public MatchDecisionStatus Decision { get; set; }

        [MaxLength(1000)]
        public string? DecisionReason { get; set; }

        public Guid DecidedBy { get; set; }

        public DateTime DecidedAt { get; set; }

        [MaxLength(1000)]
        public string? BankLineDescription { get; set; }

        [MaxLength(1000)]
        public string? SystemEntityDescription { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? Amount { get; set; }

        [MaxLength(50)]
        public string? MatchType { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Guid? CreatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public Guid? UpdatedBy { get; set; }

        public MatchGroup MatchGroup { get; set; } = default!;
    }

    public class ReconciliationException
    {
        [Key]
        public Guid ReconciliationExceptionId { get; set; }

        [ForeignKey(nameof(ReconciliationRun))]
        public Guid ReconciliationRunId { get; set; }

        public Guid? BankStatementLineId { get; set; }

        public Guid? CanonicalTransactionId { get; set; }

        [Required]
        [MaxLength(1000)]
        public string Reason { get; set; } = string.Empty;

        public ReconciliationExceptionStatus Status { get; set; } = ReconciliationExceptionStatus.Open;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Guid? CreatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public Guid? UpdatedBy { get; set; }

        public ReconciliationRun ReconciliationRun { get; set; } = default!;
    }

    public class ReportSnapshot
    {
        [Key]
        public Guid ReportSnapshotId { get; set; }

        public DateTime SnapshotDate { get; set; }

        public int TotalUnmatchedCashouts { get; set; }

        public int PendingCardMatches { get; set; }

        [Column(TypeName = "decimal(5,2)")]
        public decimal ReconciliationCompletionPercentage { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Guid? CreatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public Guid? UpdatedBy { get; set; }
    }

    public enum ReconciliationRunStatus
    {
        InProgress = 0,
        Completed = 1,
        Failed = 2
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
        Rejected = 1,
        Pending = 2
    }

    public enum ReconciliationExceptionStatus
    {
        Open = 0,
        Investigating = 1,
        Resolved = 2
    }
}
