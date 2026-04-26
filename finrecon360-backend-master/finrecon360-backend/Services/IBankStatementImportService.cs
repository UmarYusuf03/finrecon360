using finrecon360_backend.Dtos.Reconciliation;

namespace finrecon360_backend.Services
{
    /// <summary>
    /// Contract for bank statement import operations.
    /// </summary>
    public interface IBankStatementImportService
    {
        /// <summary>
        /// Uploads a bank statement and creates an import record.
        /// </summary>
        /// <param name="request">Upload request payload.</param>
        /// <param name="currentUserId">Authenticated user identifier.</param>
        /// <returns>Created import response.</returns>
        Task<StatementImportResponse> UploadStatementAsync(UploadStatementRequest request, Guid currentUserId);

        /// <summary>
        /// Gets paginated imports for a specific bank account.
        /// </summary>
        /// <param name="bankAccountId">Bank account identifier.</param>
        /// <param name="pageNumber">1-based page number.</param>
        /// <param name="pageSize">Number of items per page.</param>
        /// <returns>Paginated import responses.</returns>
        Task<PaginatedResponse<StatementImportResponse>> GetImportsAsync(Guid bankAccountId, int pageNumber, int pageSize);

        /// <summary>
        /// Gets an import by its identifier.
        /// </summary>
        /// <param name="id">Import identifier.</param>
        /// <returns>Import response if found; otherwise null.</returns>
        Task<StatementImportResponse?> GetImportByIdAsync(Guid id);
    }
}