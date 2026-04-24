using finrecon360_backend.Dtos.Reconciliation;

namespace finrecon360_backend.Services
{
    /// <summary>
    /// Contract for reconciliation run operations.
    /// </summary>
    public interface IReconciliationService
    {
        /// <summary>
        /// Creates a new reconciliation run for a bank account.
        /// </summary>
        /// <param name="request">Request containing run creation details.</param>
        /// <param name="currentUserId">Identifier of the authenticated user creating the run.</param>
        /// <returns>The created reconciliation run.</returns>
        Task<ReconciliationRunResponse> CreateRunAsync(CreateReconciliationRunRequest request, Guid currentUserId);

        /// <summary>
        /// Retrieves reconciliation runs in paginated form.
        /// </summary>
        /// <param name="pageNumber">1-based page number.</param>
        /// <param name="pageSize">Number of items per page.</param>
        /// <returns>A paginated list of reconciliation runs.</returns>
        Task<PaginatedResponse<ReconciliationRunResponse>> GetRunsAsync(int pageNumber, int pageSize);

        /// <summary>
        /// Retrieves a reconciliation run by its identifier.
        /// </summary>
        /// <param name="id">Reconciliation run identifier.</param>
        /// <returns>The reconciliation run if found; otherwise null.</returns>
        Task<ReconciliationRunResponse?> GetRunByIdAsync(Guid id);
    }
}