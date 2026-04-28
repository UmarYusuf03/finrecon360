using System.Security.Claims;
using finrecon360_backend.Authorization;
using finrecon360_backend.Dtos.Reconciliation;
using finrecon360_backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace finrecon360_backend.Controllers.Admin
{
    /// <summary>
    /// API endpoints for managing reconciliation runs.
    /// </summary>
    [ApiController]
    [Route("api/tenant-admin/reconciliation/runs")]
    [Authorize]
    public class ReconciliationController : ControllerBase
    {
        private readonly IReconciliationService _reconciliationService;
        private readonly IMatchingEngineService _matchingEngineService;
        private readonly ILogger<ReconciliationController> _logger;
        private readonly Data.AppDbContext _dbContext;
        private readonly Services.ITenantContext _tenantContext;

        /// <summary>
        /// Initializes a new instance of the <see cref="ReconciliationController"/> class.
        /// </summary>
        /// <param name="reconciliationService">Reconciliation service dependency.</param>
        /// <param name="matchingEngineService">Matching engine service dependency.</param>
        /// <param name="logger">Logger dependency.</param>
        public ReconciliationController(
            IReconciliationService reconciliationService,
            IMatchingEngineService matchingEngineService,
            ILogger<ReconciliationController> logger,
            Data.AppDbContext dbContext,
            Services.ITenantContext tenantContext)
        {
            _reconciliationService = reconciliationService;
            _matchingEngineService = matchingEngineService;
            _logger = logger;
            _dbContext = dbContext;
            _tenantContext = tenantContext;
        }

        /// <summary>
        /// Creates a new reconciliation run.
        /// </summary>
        /// <param name="request">Creation request payload.</param>
        /// <returns>The created reconciliation run.</returns>
        [HttpPost]
        public async Task<ActionResult<ReconciliationRunResponse>> CreateRun([FromBody] CreateReconciliationRunRequest request)
        {
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(userIdClaim, out var currentUserId))
            {
                _logger.LogWarning("CreateRun rejected because user identifier claim is missing or invalid.");
                return Unauthorized();
            }

            var createdRun = await _reconciliationService.CreateRunAsync(request, currentUserId);

            return CreatedAtAction(
                nameof(GetRunById),
                new { id = createdRun.ReconciliationRunId },
                createdRun);
        }

        /// <summary>
        /// Retrieves reconciliation runs in paginated form.
        /// </summary>
        /// <param name="pageNumber">1-based page number.</param>
        /// <param name="pageSize">Items per page.</param>
        /// <returns>Paginated reconciliation runs.</returns>
        [HttpGet]
        public async Task<ActionResult<PaginatedResponse<ReconciliationRunResponse>>> GetRuns(
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 20)
        {
            var runs = await _reconciliationService.GetRunsAsync(pageNumber, pageSize);
            return Ok(runs);
        }

        /// <summary>
        /// Retrieves a reconciliation run by id.
        /// </summary>
        /// <param name="id">Reconciliation run identifier.</param>
        /// <returns>The reconciliation run if found.</returns>
        [HttpGet("{id:guid}")]
        public async Task<ActionResult<ReconciliationRunResponse>> GetRunById(Guid id)
        {
            var run = await _reconciliationService.GetRunByIdAsync(id);
            if (run is null)
            {
                return NotFound();
            }

            return Ok(run);
        }

        /// <summary>
        /// Runs the automated matching engine for a bank statement import.
        /// </summary>
        /// <param name="request">Matching request payload.</param>
        /// <returns>Matching summary.</returns>
        [HttpPost("~/api/tenant-admin/reconciliation/run-automated-matching")]
        public async Task<ActionResult<MatchingSummaryResponse>> RunAutomatedMatching([FromBody] RunMatchingRequest request)
        {
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(userIdClaim, out var userId))
            {
                _logger.LogWarning("RunAutomatedMatching rejected because user identifier claim is missing or invalid.");
                return Unauthorized();
            }

            var summary = await _matchingEngineService.RunAutomatedMatchingAsync(request.BankStatementImportId, userId);
            return Ok(summary);
        }

        /// <summary>
        /// Returns proposed match groups that require human review.
        /// </summary>
        [HttpGet("~/api/tenant-admin/reconciliation/proposed-match-groups")]
        public async Task<ActionResult<IEnumerable<MatchGroupDto>>> GetProposedMatchGroups()
        {
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(userIdClaim, out var userId))
            {
                _logger.LogWarning("GetProposedMatchGroups rejected because user identifier claim is missing or invalid.");
                return Unauthorized();
            }

            // Resolve tenant context to enforce tenant boundary (System Admin fallback handled in service layer via ignore filters elsewhere)
            // For listing proposed groups, we mirror that behavior: allow system-level resolution to fall back conservatively.
            // Use the reconciliation service to fetch runs and groups if available; otherwise query DB directly.

            // Resolve tenant context and fetch proposed match groups within tenant boundary
            var tenantResolution = await _tenantContext.ResolveAsync();
            var effectiveTenantId = tenantResolution?.TenantId ?? Guid.Empty;

            var groups = await _dbContext.MatchGroups
                .IgnoreQueryFilters()
                .Where(x => x.Status == Models.MatchGroupStatus.Proposed && x.TenantId == effectiveTenantId)
                .Include(x => x.MatchDecisions)
                .Select(x => new MatchGroupDto
                {
                    Id = x.Id,
                    ReconciliationRunId = x.ReconciliationRunId,
                    MatchConfidenceScore = x.MatchConfidenceScore,
                    Status = x.Status,
                    MatchDecisions = x.MatchDecisions.Select(d => new MatchDecisionDto
                    {
                        Id = d.Id,
                        MatchGroupId = d.MatchGroupId,
                        Decision = d.Decision,
                        DecisionReason = d.DecisionReason,
                        DecidedBy = d.DecidedBy,
                        DecidedAt = d.DecidedAt,
                        BankLineDescription = d.BankLineDescription,
                        SystemEntityDescription = d.SystemEntityDescription,
                        Amount = d.Amount,
                        MatchType = d.MatchType
                    }).ToList()
                })
                .ToListAsync();

            return Ok(groups);
        }

        /// <summary>
        /// Returns bank statement lines that are not reconciled (exceptions) for the tenant.
        /// </summary>
        [HttpGet("~/api/tenant-admin/reconciliation/exceptions")]
        public async Task<ActionResult<IEnumerable<BankStatementLineDto>>> GetExceptions()
        {
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(userIdClaim, out var userId))
            {
                _logger.LogWarning("GetExceptions rejected because user identifier claim is missing or invalid.");
                return Unauthorized();
            }

            var tenantResolution = await _tenantContext.ResolveAsync();
            var effectiveTenantId = tenantResolution?.TenantId ?? Guid.Empty;

            var lines = await _dbContext.BankStatementLines
                .IgnoreQueryFilters()
                .Where(x => !x.IsReconciled && x.TenantId == effectiveTenantId)
                .Select(x => new BankStatementLineDto
                {
                    Id = x.Id,
                    BankStatementImportId = x.BankStatementImportId,
                    TransactionDate = x.TransactionDate,
                    PostingDate = x.PostingDate,
                    ReferenceNumber = x.ReferenceNumber,
                    Description = x.Description,
                    Amount = x.Amount,
                    IsReconciled = x.IsReconciled
                })
                .ToListAsync();

            return Ok(lines);
        }

        /// <summary>
        /// Confirms a batch of proposed match groups, marking them as finalized.
        /// </summary>
        /// <param name="request">Confirmation request payload containing match group IDs.</param>
        /// <returns>Confirmation summary.</returns>
        [HttpPost("~/api/tenant-admin/reconciliation/confirm-matches")]
        [RequirePermission("MATCHER.CONFIRM")]
        public async Task<ActionResult<ConfirmMatchesResponse>> ConfirmMatches([FromBody] ConfirmMatchesRequest request)
        {
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(userIdClaim, out var userId))
            {
                _logger.LogWarning("ConfirmMatches rejected because user identifier claim is missing or invalid.");
                return Unauthorized();
            }

            var result = await _matchingEngineService.ConfirmMatchesAsync(request.MatchGroupIds, userId);
            return Ok(result);
        }
    }
}