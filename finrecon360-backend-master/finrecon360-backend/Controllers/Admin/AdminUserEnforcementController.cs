using finrecon360_backend.Authorization;
using finrecon360_backend.Dtos.Admin;
using finrecon360_backend.Models;
using finrecon360_backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace finrecon360_backend.Controllers.Admin
{
    [ApiController]
    [Route("api/system/enforcement/tenants/{tenantId:guid}/users")]
    [Authorize]
    [RequirePermission("ADMIN.ENFORCEMENT.MANAGE")]
    [EnableRateLimiting("admin")]
    public class AdminUserEnforcementController : ControllerBase
    {
        private readonly ISystemEnforcementService _systemEnforcementService;

        public AdminUserEnforcementController(ISystemEnforcementService systemEnforcementService)
        {
            _systemEnforcementService = systemEnforcementService;
        }

        [HttpPost("{userId:guid}/suspend")]
        public async Task<IActionResult> SuspendUser(Guid tenantId, Guid userId, [FromBody] EnforcementActionRequest request)
        {
            var result = await _systemEnforcementService.ApplyUserActionAsync(
                tenantId,
                userId,
                UserStatus.Suspended,
                EnforcementActionType.Suspend,
                request,
                HttpContext.RequestAborted);

            return result switch
            {
                EnforcementApplyResult.Success => NoContent(),
                EnforcementApplyResult.Unauthorized => Unauthorized(),
                EnforcementApplyResult.NotFound => NotFound(),
                _ => BadRequest(new { message = "User enforcement request is invalid for the target tenant/user." })
            };
        }

        [HttpPost("{userId:guid}/ban")]
        public async Task<IActionResult> BanUser(Guid tenantId, Guid userId, [FromBody] EnforcementActionRequest request)
        {
            var result = await _systemEnforcementService.ApplyUserActionAsync(
                tenantId,
                userId,
                UserStatus.Banned,
                EnforcementActionType.Ban,
                request,
                HttpContext.RequestAborted);

            return result switch
            {
                EnforcementApplyResult.Success => NoContent(),
                EnforcementApplyResult.Unauthorized => Unauthorized(),
                EnforcementApplyResult.NotFound => NotFound(),
                _ => BadRequest(new { message = "User enforcement request is invalid for the target tenant/user." })
            };
        }

        [HttpPost("{userId:guid}/reinstate")]
        public async Task<IActionResult> ReinstateUser(Guid tenantId, Guid userId)
        {
            var result = await _systemEnforcementService.ReinstateUserAsync(tenantId, userId, HttpContext.RequestAborted);
            return result switch
            {
                EnforcementApplyResult.Success => NoContent(),
                EnforcementApplyResult.Unauthorized => Unauthorized(),
                EnforcementApplyResult.NotFound => NotFound(),
                _ => BadRequest(new { message = "Only suspended users in active tenants can be reinstated." })
            };
        }
    }
}
