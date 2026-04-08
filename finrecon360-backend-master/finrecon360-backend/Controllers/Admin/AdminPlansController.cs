using finrecon360_backend.Authorization;
using finrecon360_backend.Data;
using finrecon360_backend.Dtos.Admin;
using finrecon360_backend.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace finrecon360_backend.Controllers.Admin
{
    [ApiController]
    [Route("api/system/plans")]
    [Authorize]
    [RequirePermission("ADMIN.PLANS.MANAGE")]
    [EnableRateLimiting("admin")]
    public class AdminPlansController : ControllerBase
    {
        private readonly AppDbContext _dbContext;

        public AdminPlansController(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        [HttpGet]
        public async Task<ActionResult<IReadOnlyList<PlanSummaryDto>>> GetPlans()
        {
            var plans = await _dbContext.Plans
                .AsNoTracking()
                .OrderBy(p => p.PriceCents)
                .Select(p => new PlanSummaryDto(
                    p.PlanId,
                    p.Code,
                    p.Name,
                    p.PriceCents,
                    p.Currency,
                    p.DurationDays,
                    p.MaxAccounts,
                    p.IsActive))
                .ToListAsync();

            return Ok(plans);
        }

        [HttpPost]
        public async Task<ActionResult<PlanSummaryDto>> CreatePlan([FromBody] PlanCreateRequest request)
        {
            var code = request.Code.Trim().ToUpperInvariant();
            var name = request.Name.Trim();

            var exists = await _dbContext.Plans.AnyAsync(p => p.Code == code || p.Name == name);
            if (exists)
            {
                return Conflict(new { message = "Plan code or name already exists." });
            }

            var plan = new Plan
            {
                PlanId = Guid.NewGuid(),
                Code = code,
                Name = name,
                PriceCents = request.PriceCents,
                Currency = request.Currency.Trim().ToUpperInvariant(),
                DurationDays = request.DurationDays,
                MaxAccounts = request.MaxAccounts,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.Plans.Add(plan);
            await _dbContext.SaveChangesAsync();

            return CreatedAtAction(nameof(GetPlans), new PlanSummaryDto(
                plan.PlanId,
                plan.Code,
                plan.Name,
                plan.PriceCents,
                plan.Currency,
                plan.DurationDays,
                plan.MaxAccounts,
                plan.IsActive));
        }

        [HttpPut("{planId:guid}")]
        public async Task<ActionResult<PlanSummaryDto>> UpdatePlan(Guid planId, [FromBody] PlanUpdateRequest request)
        {
            var plan = await _dbContext.Plans.FirstOrDefaultAsync(p => p.PlanId == planId);
            if (plan == null)
            {
                return NotFound();
            }

            var code = request.Code.Trim().ToUpperInvariant();
            var name = request.Name.Trim();

            var duplicate = await _dbContext.Plans
                .AnyAsync(p => p.PlanId != planId && (p.Code == code || p.Name == name));
            if (duplicate)
            {
                return Conflict(new { message = "Plan code or name already exists." });
            }

            plan.Code = code;
            plan.Name = name;
            plan.PriceCents = request.PriceCents;
            plan.Currency = request.Currency.Trim().ToUpperInvariant();
            plan.DurationDays = request.DurationDays;
            plan.MaxAccounts = request.MaxAccounts;

            await _dbContext.SaveChangesAsync();

            return Ok(new PlanSummaryDto(
                plan.PlanId,
                plan.Code,
                plan.Name,
                plan.PriceCents,
                plan.Currency,
                plan.DurationDays,
                plan.MaxAccounts,
                plan.IsActive));
        }

        [HttpPost("{planId:guid}/deactivate")]
        public async Task<IActionResult> DeactivatePlan(Guid planId)
        {
            var plan = await _dbContext.Plans.FirstOrDefaultAsync(p => p.PlanId == planId);
            if (plan == null)
            {
                return NotFound();
            }

            plan.IsActive = false;
            await _dbContext.SaveChangesAsync();
            return NoContent();
        }

        [HttpPost("{planId:guid}/activate")]
        public async Task<IActionResult> ActivatePlan(Guid planId)
        {
            var plan = await _dbContext.Plans.FirstOrDefaultAsync(p => p.PlanId == planId);
            if (plan == null)
            {
                return NotFound();
            }

            plan.IsActive = true;
            await _dbContext.SaveChangesAsync();
            return NoContent();
        }
    }
}
