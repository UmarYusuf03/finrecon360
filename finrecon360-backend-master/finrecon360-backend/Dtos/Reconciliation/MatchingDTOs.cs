namespace finrecon360_backend.Dtos.Reconciliation
{
    /// <summary>
    /// Request payload to start an automated matching run.
    /// </summary>
    public class RunMatchingRequest
    {
        public Guid BankStatementImportId { get; set; }
    }

    /// <summary>
    /// Summary response for an automated matching run.
    /// </summary>
    public class MatchingSummaryResponse
    {
        public int TotalLinesProcessed { get; set; }
        public int MatchesFound { get; set; }
        public int GeneralLedgerMatches { get; set; }
        public int InvoiceMatches { get; set; }
        public int PayoutMatches { get; set; }
    }

    /// <summary>
    /// Request payload to confirm a set of proposed match groups.
    /// </summary>
    public class ConfirmMatchesRequest
    {
        /// <summary>
        /// Collection of match group IDs to confirm.
        /// </summary>
        public List<Guid> MatchGroupIds { get; set; } = new();
    }

    /// <summary>
    /// Response from confirming matches.
    /// </summary>
    public class ConfirmMatchesResponse
    {
        /// <summary>
        /// Number of match groups confirmed.
        /// </summary>
        public int TotalConfirmed { get; set; }

        /// <summary>
        /// Number of linked reconciliations marked as finalized.
        /// </summary>
        public int TotalReconciliationsFinalized { get; set; }
    }
}
