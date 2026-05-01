using finrecon360_backend.Models;

namespace finrecon360_backend.Dtos.Reconciliation
{
    public class BankStatementImportDto
    {
        public Guid Id { get; set; }
        public Guid BatchId { get; set; }
        public Guid BankAccountId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public DateTime ImportDate { get; set; }
        public string Status { get; set; } = string.Empty;
        public int TotalRows { get; set; }
        public int ValidRows { get; set; }
        public List<BankStatementLineDto> BankStatementLines { get; set; } = new();
    }

    public class BankStatementLineDto
    {
        public Guid Id { get; set; }
        public Guid BankStatementImportId { get; set; }
        public DateTime TransactionDate { get; set; }
        public DateTime? PostingDate { get; set; }
        public string? ReferenceNumber { get; set; }
        public string? Description { get; set; }
        public decimal Amount { get; set; }
        public bool IsReconciled { get; set; }
    }

    public class ReconciliationRunDto
    {
        public Guid Id { get; set; }
        public DateTime RunDate { get; set; }
        public Guid BankAccountId { get; set; }
        public ReconciliationRunStatus Status { get; set; }
        public int TotalMatchesProposed { get; set; }
        public List<MatchGroupDto> MatchGroups { get; set; } = new();
        public List<ReconciliationExceptionDto> Exceptions { get; set; } = new();
    }

    public class MatchGroupDto
    {
        public Guid Id { get; set; }
        public Guid ReconciliationRunId { get; set; }
        public decimal? MatchConfidenceScore { get; set; }
        public MatchGroupStatus Status { get; set; }
        public List<MatchDecisionDto> MatchDecisions { get; set; } = new();
    }

    public class MatchDecisionDto
    {
        public Guid Id { get; set; }
        public Guid MatchGroupId { get; set; }
        public MatchDecisionStatus Decision { get; set; }
        public string? DecisionReason { get; set; }
        public Guid DecidedBy { get; set; }
        public DateTime DecidedAt { get; set; }
        public string? BankLineDescription { get; set; }
        public string? SystemEntityDescription { get; set; }
        public decimal? Amount { get; set; }
        public string? MatchType { get; set; }
    }

    public class ReconciliationExceptionDto
    {
        public Guid Id { get; set; }
        public Guid ReconciliationRunId { get; set; }
        public Guid? BankStatementLineId { get; set; }
        public Guid? CanonicalTransactionId { get; set; }
        public string Reason { get; set; } = string.Empty;
        public ReconciliationExceptionStatus Status { get; set; }
    }

    public class ReportSnapshotDto
    {
        public Guid Id { get; set; }
        public DateTime SnapshotDate { get; set; }
        public int TotalUnmatchedCashouts { get; set; }
        public int PendingCardMatches { get; set; }
        public decimal ReconciliationCompletionPercentage { get; set; }
    }

    public class CreateReconciliationRunRequest
    {
        public Guid BankAccountId { get; set; }
    }

    public class ReconciliationRunResponse
    {
        public Guid ReconciliationRunId { get; set; }
        public DateTime RunDate { get; set; }
        public Guid BankAccountId { get; set; }
        public string Status { get; set; } = string.Empty;
        public int TotalMatchesProposed { get; set; }
        public int TotalMatchesConfirmed { get; set; }
        public int TotalExceptions { get; set; }
    }

    public class PaginatedResponse<T>
    {
        public IEnumerable<T> Items { get; set; } = Array.Empty<T>();
        public int TotalCount { get; set; }
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
    }
}
