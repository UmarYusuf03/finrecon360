namespace finrecon360_backend.Dtos.Reconciliation
{
    /// <summary>
    /// Request payload to start an automated matching run.
    /// </summary>
    public class RunMatchingRequest
    {
        // In tenant data-plane architecture this maps to ImportBatchId.
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
        public List<Guid> MatchGroupIds { get; set; } = new();
    }

    /// <summary>
    /// Response from confirming matches.
    /// </summary>
    public class ConfirmMatchesResponse
    {
        public int TotalConfirmed { get; set; }
        public int TotalReconciliationsFinalized { get; set; }
    }
}
