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

        /// <summary>
        /// Confirms a batch of proposed match groups, marking them as finalized.
        /// </summary>
        /// <param name="matchGroupIds">Collection of match group IDs to confirm.</param>
        /// <param name="currentUserId">Authenticated user identifier.</param>
        /// <returns>Confirmation summary.</returns>
        Task<ConfirmMatchesResponse> ConfirmMatchesAsync(List<Guid> matchGroupIds, Guid currentUserId);
    }
}
