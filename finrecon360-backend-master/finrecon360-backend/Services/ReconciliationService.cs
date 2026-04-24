using finrecon360_backend.Data;
using finrecon360_backend.Dtos.Reconciliation;
using finrecon360_backend.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace finrecon360_backend.Services
{
    /// <summary>
    /// Service implementation for reconciliation run operations.
    /// </summary>
    public class ReconciliationService : IReconciliationService
    {
        private readonly AppDbContext _dbContext;
        private readonly ILogger<ReconciliationService> _logger;
        private readonly ITenantContext _tenantContext;

        /// <summary>
        /// Initializes a new instance of the <see cref="ReconciliationService"/> class.
        /// </summary>
        /// <param name="dbContext">Application database context.</param>
        /// <param name="logger">Logger instance.</param>
        /// <param name="tenantContext">Tenant context used to resolve current tenant.</param>
        public ReconciliationService(
            AppDbContext dbContext,
            ILogger<ReconciliationService> logger,
            ITenantContext tenantContext)
        {
            _dbContext = dbContext;
            _logger = logger;
            _tenantContext = tenantContext;
        }

        /// <summary>
        /// Creates a new reconciliation run.
        /// </summary>
        /// <param name="request">Run creation request.</param>
        /// <param name="currentUserId">Authenticated user creating the run.</param>
        /// <returns>The created run mapped to response DTO.</returns>
        public async Task<ReconciliationRunResponse> CreateRunAsync(CreateReconciliationRunRequest request, Guid currentUserId)
        {
            var now = DateTime.UtcNow;

            var run = new ReconciliationRun
            {
                // Id is intentionally not set; EF handles value generation for Guid PK.
                BankAccountId = request.BankAccountId,
                RunDate = now,
                Status = ReconciliationRunStatus.InProgress,

                // New run defaults.
                TotalMatchesProposed = 0,

                // Audit fields.
                CreatedAt = now,
                CreatedBy = currentUserId
            };

            var tenantResolution = await _tenantContext.ResolveAsync();
            if (tenantResolution != null)
            {
                run.TenantId = tenantResolution.TenantId;
            }

            _dbContext.ReconciliationRuns.Add(run);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation(
                "Reconciliation run created. RunId={RunId}, BankAccountId={BankAccountId}, CreatedBy={CreatedBy}",
                run.Id,
                run.BankAccountId,
                currentUserId);

            return MapRunToResponse(run, totalMatchesConfirmed: 0, totalExceptions: 0);
        }

        /// <summary>
        /// Returns paginated reconciliation runs ordered by most recent run date.
        /// </summary>
        /// <param name="pageNumber">1-based page number.</param>
        /// <param name="pageSize">Page size.</param>
        /// <returns>Paginated reconciliation run response.</returns>
        public async Task<PaginatedResponse<ReconciliationRunResponse>> GetRunsAsync(int pageNumber, int pageSize)
        {
            // Defensive bounds to avoid invalid Skip/Take behavior.
            pageNumber = pageNumber < 1 ? 1 : pageNumber;
            pageSize = pageSize < 1 ? 20 : pageSize;

            var baseQuery = _dbContext.ReconciliationRuns
                .AsNoTracking();

            var totalCount = await baseQuery.CountAsync();

            var items = await baseQuery
                .OrderByDescending(r => r.RunDate)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .Select(r => new ReconciliationRunResponse
                {
                    ReconciliationRunId = r.Id,
                    RunDate = r.RunDate,
                    BankAccountId = r.BankAccountId,
                    Status = r.Status.ToString(),
                    TotalMatchesProposed = r.TotalMatchesProposed,
                    TotalMatchesConfirmed = r.MatchGroups.Count(m => m.Status == MatchGroupStatus.Confirmed),
                    TotalExceptions = r.Exceptions.Count()
                })
                .ToListAsync();

            return new PaginatedResponse<ReconciliationRunResponse>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }

        /// <summary>
        /// Returns one reconciliation run by id.
        /// </summary>
        /// <param name="id">Run id.</param>
        /// <returns>Mapped DTO if found; otherwise null.</returns>
        public async Task<ReconciliationRunResponse?> GetRunByIdAsync(Guid id)
        {
            return await _dbContext.ReconciliationRuns
                .AsNoTracking()
                .Where(r => r.Id == id)
                .Select(r => new ReconciliationRunResponse
                {
                    ReconciliationRunId = r.Id,
                    RunDate = r.RunDate,
                    BankAccountId = r.BankAccountId,
                    Status = r.Status.ToString(),
                    TotalMatchesProposed = r.TotalMatchesProposed,
                    TotalMatchesConfirmed = r.MatchGroups.Count(m => m.Status == MatchGroupStatus.Confirmed),
                    TotalExceptions = r.Exceptions.Count()
                })
                .FirstOrDefaultAsync();
        }

        private static ReconciliationRunResponse MapRunToResponse(
            ReconciliationRun run,
            int totalMatchesConfirmed,
            int totalExceptions)
        {
            return new ReconciliationRunResponse
            {
                ReconciliationRunId = run.Id,
                RunDate = run.RunDate,
                BankAccountId = run.BankAccountId,
                Status = run.Status.ToString(),
                TotalMatchesProposed = run.TotalMatchesProposed,
                TotalMatchesConfirmed = totalMatchesConfirmed,
                TotalExceptions = totalExceptions
            };
        }

    }
}