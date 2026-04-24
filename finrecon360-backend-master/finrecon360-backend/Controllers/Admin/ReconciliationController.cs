using System.Security.Claims;
using finrecon360_backend.Dtos.Reconciliation;
using finrecon360_backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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
        private readonly ILogger<ReconciliationController> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="ReconciliationController"/> class.
        /// </summary>
        /// <param name="reconciliationService">Reconciliation service dependency.</param>
        /// <param name="logger">Logger dependency.</param>
        public ReconciliationController(
            IReconciliationService reconciliationService,
            ILogger<ReconciliationController> logger)
        {
            _reconciliationService = reconciliationService;
            _logger = logger;
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
    }
}