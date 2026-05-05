using finrecon360_backend.Data;
using finrecon360_backend.Dtos.Public;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace finrecon360_backend.Controllers.Public
{
    /// <summary>
    /// WHY: Exposes subscription plan catalog to unauthenticated users during onboarding.
    /// Returns only active plans with pricing, max users, and max bank accounts so prospective tenants
    /// can see plan options before registering. Kept public (no auth required) to allow window-shopping.
    /// </summary>
    [ApiController]
    [Route("api/public/plans")]
    public class PlansController : ControllerBase
    {
        private readonly AppDbContext _dbContext;

        public PlansController(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        [HttpGet]
        public async Task<ActionResult<IReadOnlyList<PublicPlanSummaryDto>>> GetPlans()
        {
            var plans = await _dbContext.Plans
                .AsNoTracking()
                .Where(p => p.IsActive)
                .OrderBy(p => p.PriceCents)
                .Select(p => new PublicPlanSummaryDto(
                    p.PlanId,
                    p.Code,
                    p.Name,
                    p.PriceCents,
                    p.Currency,
                    p.DurationDays,
                    p.MaxUsers,
                    p.MaxAccounts))
                .ToListAsync();

            return Ok(plans);
        }
    }
}
