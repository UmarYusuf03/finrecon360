using finrecon360_backend.Data;
using finrecon360_backend.Dtos.Subscriptions;
using finrecon360_backend.Dtos.Me;
using finrecon360_backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using finrecon360_backend.Authorization;

namespace finrecon360_backend.Controllers
{
    [ApiController]
    [Route("api/me")]
    [Authorize]
    [EnableRateLimiting("me")]
    public class MeController : ControllerBase
    {
        private readonly AppDbContext _dbContext;
        private readonly IUserContext _userContext;
        private readonly IPermissionService _permissionService;
<<<<<<< Updated upstream

        public MeController(AppDbContext dbContext, IUserContext userContext, IPermissionService permissionService)
=======
        private readonly ITenantContext _tenantContext;
        private readonly ITenantDbContextFactory _tenantDbContextFactory;
        private readonly ISubscriptionService _subscriptionService;

        public MeController(
            AppDbContext dbContext,
            IUserContext userContext,
            IPermissionService permissionService,
            ITenantContext tenantContext,
            ITenantDbContextFactory tenantDbContextFactory,
            ISubscriptionService subscriptionService)
>>>>>>> Stashed changes
        {
            _dbContext = dbContext;
            _userContext = userContext;
            _permissionService = permissionService;
<<<<<<< Updated upstream
=======
            _tenantContext = tenantContext;
            _tenantDbContextFactory = tenantDbContextFactory;
            _subscriptionService = subscriptionService;
>>>>>>> Stashed changes
        }

        [HttpGet]
        public async Task<ActionResult<MeResponse>> Get()
        {
            if (_userContext.UserId is not { } userId)
            {
                return Unauthorized();
            }

            var user = await _dbContext.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.UserId == userId);

            if (user is null)
            {
                return NotFound();
            }

            if (!user.IsActive)
            {
                return Forbid();
            }

            var displayName = user.DisplayName ?? $"{user.FirstName} {user.LastName}".Trim();
            var roles = await _permissionService.GetRolesForUserAsync(userId);
            var permissions = await _permissionService.GetPermissionsForUserAsync(userId);

            return Ok(new MeResponse(
                user.UserId,
                user.Email,
                displayName,
                roles.ToList(),
                permissions.OrderBy(p => p).ToList()));
        }

        [HttpGet("subscription")]
        [RequirePermission("PROFILE.SUBSCRIPTION.VIEW")]
        public async Task<ActionResult<SubscriptionOverviewDto>> GetSubscriptionOverview(CancellationToken cancellationToken)
        {
            if (_userContext.UserId is not { } userId)
            {
                return Unauthorized();
            }

            var tenant = await _tenantContext.ResolveAsync();
            if (tenant == null)
            {
                return NotFound();
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

        [HttpPost("subscription/checkout")]
        [RequirePermission("PROFILE.SUBSCRIPTION.CHANGE")]
        public async Task<ActionResult<SubscriptionCheckoutResponse>> CreateSubscriptionCheckout(
            [FromBody] SubscriptionChangeRequest request,
            CancellationToken cancellationToken)
        {
            if (_userContext.UserId is not { } userId)
            {
                return Unauthorized();
            }

            var tenant = await _tenantContext.ResolveAsync();
            if (tenant == null)
            {
                return NotFound();
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
