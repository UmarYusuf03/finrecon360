using finrecon360_backend.Authorization;
using finrecon360_backend.Data;
using finrecon360_backend.Dtos.Subscriptions;
using finrecon360_backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace finrecon360_backend.Controllers.ControlPlane
{
    [ApiController]
    [Route("api/system/tenants/{tenantId:guid}/subscription")]
    [Authorize]
    [RequirePermission("ADMIN.SUBSCRIPTIONS.MANAGE")]
    [EnableRateLimiting("admin")]
    public class SystemTenantSubscriptionsController : ControllerBase
    {
        private readonly AppDbContext _dbContext;
        private readonly IUserContext _userContext;
        private readonly ISubscriptionService _subscriptionService;

        public SystemTenantSubscriptionsController(
            AppDbContext dbContext,
            IUserContext userContext,
            ISubscriptionService subscriptionService)
        {
            _dbContext = dbContext;
            _userContext = userContext;
            _subscriptionService = subscriptionService;
        }

        [HttpGet]
        public async Task<ActionResult<SubscriptionOverviewDto>> GetOverview(Guid tenantId, CancellationToken cancellationToken)
        {
            var exists = await _dbContext.Tenants.AsNoTracking().AnyAsync(t => t.TenantId == tenantId, cancellationToken);
            if (!exists)
            {
                return NotFound();
            }

            try
            {
                var overview = await _subscriptionService.GetOverviewAsync(tenantId, cancellationToken);
                return Ok(overview);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("checkout")]
        public async Task<ActionResult<SubscriptionCheckoutResponse>> CreateCheckout(Guid tenantId, [FromBody] SubscriptionChangeRequest request, CancellationToken cancellationToken)
        {
            if (_userContext.UserId is not { } userId)
            {
                return Unauthorized();
            }

            var exists = await _dbContext.Tenants.AsNoTracking().AnyAsync(t => t.TenantId == tenantId, cancellationToken);
            if (!exists)
            {
                return NotFound();
            }

            try
            {
                var result = await _subscriptionService.CreateCheckoutAsync(tenantId, userId, request.PlanId, cancellationToken);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}