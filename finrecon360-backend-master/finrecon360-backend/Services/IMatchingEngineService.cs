using finrecon360_backend.Dtos.Reconciliation;

namespace finrecon360_backend.Services
{
    /// <summary>
    /// Contract for automated reconciliation matching.
    /// </summary>
    public interface IMatchingEngineService
    {
        /// <summary>
        /// Runs automated matching for a bank statement import.
        /// </summary>
        /// <param name="bankStatementImportId">Bank statement import identifier.</param>
        /// <param name="currentUserId">Authenticated user identifier.</param>
        /// <returns>Matching summary for the run.</returns>
        Task<MatchingSummaryResponse> RunAutomatedMatchingAsync(Guid bankStatementImportId, Guid currentUserId);
    }
}
