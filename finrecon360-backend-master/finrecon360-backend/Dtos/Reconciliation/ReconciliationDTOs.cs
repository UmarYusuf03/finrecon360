using finrecon360_backend.Models;
using System;
using System.Collections.Generic;

namespace finrecon360_backend.Dtos.Reconciliation
{
    public class BankStatementImportDto
    {
        public Guid Id { get; set; }
        public Guid BatchId { get; set; }
        public Guid BankAccountId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public DateTime ImportDate { get; set; }
        public BankStatementImportStatus Status { get; set; }
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

    /// <summary>
    /// Request payload to create a new reconciliation run.
    /// </summary>
    public class CreateReconciliationRunRequest
    {
        /// <summary>
        /// Bank account identifier for which the run will be created.
        /// </summary>
        public Guid BankAccountId { get; set; }
    }

    /// <summary>
    /// Response model representing a reconciliation run.
    /// </summary>
    public class ReconciliationRunResponse
    {
        /// <summary>
        /// Unique identifier of the reconciliation run.
        /// </summary>
        public Guid ReconciliationRunId { get; set; }

        /// <summary>
        /// Date and time the run was created/executed.
        /// </summary>
        public DateTime RunDate { get; set; }

        /// <summary>
        /// Bank account identifier associated with the run.
        /// </summary>
        public Guid BankAccountId { get; set; }

        /// <summary>
        /// Current processing status of the run.
        /// </summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>
        /// Total number of proposed matches.
        /// </summary>
        public int TotalMatchesProposed { get; set; }

        /// <summary>
        /// Total number of confirmed matches.
        /// </summary>
        public int TotalMatchesConfirmed { get; set; }

        /// <summary>
        /// Total number of reconciliation exceptions.
        /// </summary>
        public int TotalExceptions { get; set; }
    }

    /// <summary>
    /// Generic pagination wrapper for API list responses.
    /// </summary>
    /// <typeparam name="T">Type of list item.</typeparam>
    public class PaginatedResponse<T>
    {
        /// <summary>
        /// Current page data items.
        /// </summary>
        public IEnumerable<T> Items { get; set; } = Array.Empty<T>();

        /// <summary>
        /// Total number of items across all pages.
        /// </summary>
        public int TotalCount { get; set; }

        /// <summary>
        /// Current page number (1-based).
        /// </summary>
        public int PageNumber { get; set; }

        /// <summary>
        /// Number of items requested per page.
        /// </summary>
        public int PageSize { get; set; }
    }
}