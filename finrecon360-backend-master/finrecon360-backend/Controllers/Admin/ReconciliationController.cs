using System.Security.Claims;
using finrecon360_backend.Authorization;
using finrecon360_backend.Dtos.Reconciliation;
using finrecon360_backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace finrecon360_backend.Controllers.Admin
{
    /// <summary>
    /// API endpoints for reconciliation and matching operations.
    /// </summary>
    [ApiController]
    [Route("api/admin/reconciliation/runs")]
    [Authorize]
    public class ReconciliationController : ControllerBase
    {
        private readonly IMatchingEngineService _matchingEngineService;
        private readonly ITenantContext _tenantContext;
        private readonly ILogger<ReconciliationController> _logger;

        public ReconciliationController(
            IMatchingEngineService matchingEngineService,
            ITenantContext tenantContext,
            ILogger<ReconciliationController> logger)
        {
            _matchingEngineService = matchingEngineService;
            _tenantContext = tenantContext;
            _logger = logger;
        }

        [HttpPost]
        public ActionResult<object> CreateRun([FromBody] CreateReconciliationRunRequest request)
        {
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(userIdClaim, out _))
            {
                _logger.LogWarning("CreateRun rejected because user identifier claim is missing or invalid.");
                return Unauthorized();
            }

            _ = request;
            return StatusCode(StatusCodes.Status501NotImplemented, new { message = "CreateRun service is planned for a later step." });
        }

        [HttpGet]
        public ActionResult<object> GetRuns([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20)
        {
            _ = pageNumber;
            _ = pageSize;

            return StatusCode(StatusCodes.Status501NotImplemented, new { message = "GetRuns service is planned for a later step." });
        }

        [HttpGet("{id:guid}")]
        public ActionResult<object> GetRunById(Guid id)
        {
            _ = id;

            return StatusCode(StatusCodes.Status501NotImplemented, new { message = "GetRunById service is planned for a later step." });
        }

        [HttpPost("~/api/admin/reconciliation/run-automated-matching")]
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

        [HttpGet("~/api/admin/reconciliation/proposed-match-groups")]
        public async Task<ActionResult<IEnumerable<MatchGroupDto>>> GetProposedMatchGroups()
        {
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(userIdClaim, out _))
            {
                _logger.LogWarning("GetProposedMatchGroups rejected because user identifier claim is missing or invalid.");
                return Unauthorized();
            }

            var tenantResolution = await _tenantContext.ResolveAsync();
            var effectiveTenantId = tenantResolution?.TenantId ?? Guid.Empty;
            if (effectiveTenantId == Guid.Empty)
            {
                return Forbid();
            }

            var groups = await _matchingEngineService.GetProposedMatchGroupsAsync(effectiveTenantId);
            return Ok(groups);
        }

        [HttpGet("~/api/admin/reconciliation/exceptions")]
        public async Task<ActionResult<IEnumerable<BankStatementLineDto>>> GetExceptions()
        {
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(userIdClaim, out _))
            {
                _logger.LogWarning("GetExceptions rejected because user identifier claim is missing or invalid.");
                return Unauthorized();
            }

            var tenantResolution = await _tenantContext.ResolveAsync();
            var effectiveTenantId = tenantResolution?.TenantId ?? Guid.Empty;
            if (effectiveTenantId == Guid.Empty)
            {
                return Forbid();
            }

            var exceptions = await _matchingEngineService.GetExceptionsAsync(effectiveTenantId);
            return Ok(exceptions);
        }

        [HttpPost("~/api/admin/reconciliation/confirm-matches")]
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
