using finrecon360_backend.Authorization;
using finrecon360_backend.Dtos.Subscriptions;
using finrecon360_backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace finrecon360_backend.Controllers.Admin
{
    [ApiController]
    [Route("api/admin/subscription")]
    [Authorize]
    [RequirePermission("ADMIN.SUBSCRIPTIONS.MANAGE")]
    [EnableRateLimiting("admin")]
    public class AdminSubscriptionController : ControllerBase
    {
        private readonly ITenantContext _tenantContext;
        private readonly IUserContext _userContext;
        private readonly ISubscriptionService _subscriptionService;

        public AdminSubscriptionController(
            ITenantContext tenantContext,
            IUserContext userContext,
            ISubscriptionService subscriptionService)
        {
            _tenantContext = tenantContext;
            _userContext = userContext;
            _subscriptionService = subscriptionService;
        }

        [HttpGet]
        public async Task<ActionResult<SubscriptionOverviewDto>> GetOverview(CancellationToken cancellationToken)
        {
            var tenant = await _tenantContext.ResolveAsync(cancellationToken);
            if (tenant == null)
            {
                return Forbid();
            }

            try
            {
                var overview = await _subscriptionService.GetOverviewAsync(tenant.TenantId, cancellationToken);
                return Ok(overview);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("checkout")]
        public async Task<ActionResult<SubscriptionCheckoutResponse>> CreateCheckout([FromBody] SubscriptionChangeRequest request, CancellationToken cancellationToken)
        {
            if (_userContext.UserId is not { } userId)
            {
                return Unauthorized();
            }

            var tenant = await _tenantContext.ResolveAsync(cancellationToken);
            if (tenant == null)
            {
                return Forbid();
            }

            try
            {
                var result = await _subscriptionService.CreateCheckoutAsync(tenant.TenantId, userId, request.PlanId, cancellationToken);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}