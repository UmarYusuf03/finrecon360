using finrecon360_backend.Authorization;
using finrecon360_backend.Data;
using finrecon360_backend.Dtos.CashFlow;
using finrecon360_backend.Services;
using finrecon360_backend.Services.CashFlow;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace finrecon360_backend.Controllers.Admin
{
    [ApiController]
    [Route("api/admin/cash-flow-forecast")]
    [Authorize]
    [RequirePermission("ADMIN.CASH_FLOW_FORECAST.VIEW")]
    public class CashFlowForecastController : ControllerBase
    {
        private readonly AppDbContext _dbContext;
        private readonly ITenantContext _tenantContext;
        private readonly ITenantDbContextFactory _tenantDbContextFactory;
        private readonly IUserContext _userContext;
        private readonly ICashFlowForecastService _forecastService;

        public CashFlowForecastController(
            AppDbContext dbContext,
            ITenantContext tenantContext,
            ITenantDbContextFactory tenantDbContextFactory,
            IUserContext userContext,
            ICashFlowForecastService forecastService)
        {
            _dbContext = dbContext;
            _tenantContext = tenantContext;
            _tenantDbContextFactory = tenantDbContextFactory;
            _userContext = userContext;
            _forecastService = forecastService;
        }

        [HttpGet]
        public async Task<ActionResult<CashFlowForecastResponse>> Get(
            [FromQuery] Guid? bankAccountId,
            [FromQuery] int horizonDays,
            CancellationToken ct)
        {
            var auth = await AuthorizeTenantAdminAsync(ct);
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            var clampedHorizon = horizonDays <= 0 ? 30 : Math.Clamp(horizonDays, 1, 90);
            var result = await _forecastService.GetForecastAsync(tenantDb, bankAccountId, clampedHorizon, ct);
            return Ok(result);
        }

        private async Task<(TenantDbContext? Db, ActionResult? Error)> AuthorizeTenantAdminAsync(CancellationToken ct)
        {
            if (_userContext.UserId is not { } userId) return (null, Unauthorized());

            var tenant = await _tenantContext.ResolveAsync(ct);
            if (tenant == null) return (null, Forbid());

            var isTenantMember = await _dbContext.TenantUsers.AsNoTracking()
                .AnyAsync(tu => tu.TenantId == tenant.TenantId && tu.UserId == userId, ct);
            if (!isTenantMember) return (null, Forbid());

            var tenantDb = await _tenantDbContextFactory.CreateAsync(tenant.TenantId, ct);
            var isActiveInTenant = await tenantDb.TenantUsers.AsNoTracking()
                .AnyAsync(tu => tu.UserId == userId && tu.IsActive, ct);
            if (!isActiveInTenant)
            {
                await tenantDb.DisposeAsync();
                return (null, Forbid());
            }

            return (tenantDb, null);
        }
    }
}
