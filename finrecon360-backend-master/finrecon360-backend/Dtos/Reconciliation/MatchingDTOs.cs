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
}
