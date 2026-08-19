using finrecon360_backend.Authorization;
using finrecon360_backend.Data;
using finrecon360_backend.Dtos.Admin;
using finrecon360_backend.Models;
using finrecon360_backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace finrecon360_backend.Controllers.Admin
{
    /// <summary>
    /// WHY: Surfaces missed-payment alerts raised by SubscriptionOverdueMonitorHostedService.
    /// Deliberately read/acknowledge-only from here — deciding to suspend or ban a tenant over
    /// a payment issue stays a distinct, human action on the Tenants screen, not something this
    /// controller can trigger directly.
    /// </summary>
    [ApiController]
    [Route("api/system/payment-alerts")]
    [Authorize]
    [RequirePermission("ADMIN.PAYMENT_ALERTS.VIEW")]
    [EnableRateLimiting("admin")]
    public class PaymentAlertsController : ControllerBase
    {
        private readonly AppDbContext _dbContext;
        private readonly IUserContext _userContext;
        private readonly IAuditLogger _auditLogger;

        public PaymentAlertsController(AppDbContext dbContext, IUserContext userContext, IAuditLogger auditLogger)
        {
            _dbContext = dbContext;
            _userContext = userContext;
            _auditLogger = auditLogger;
        }

        [HttpGet]
        public async Task<ActionResult<IReadOnlyList<PaymentAlertResponse>>> GetAlerts([FromQuery] string? status)
        {
            var query = _dbContext.TenantPaymentAlerts
                .AsNoTracking()
                .Include(a => a.Tenant)
                .Include(a => a.Subscription).ThenInclude(s => s.Plan)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<PaymentAlertStatus>(status, true, out var parsedStatus))
            {
                query = query.Where(a => a.Status == parsedStatus);
            }

            var alerts = await query
                .OrderByDescending(a => a.DaysOverdue)
                .Select(a => new PaymentAlertResponse(
                    a.TenantPaymentAlertId,
                    a.TenantId,
                    a.Tenant.Name,
                    a.SubscriptionId,
                    a.Subscription.Plan.Name,
                    a.PeriodEndAt,
                    a.DaysOverdue,
                    a.Status.ToString(),
                    a.CreatedAt,
                    a.AcknowledgedAt,
                    a.ResolvedAt))
                .ToListAsync();

            return Ok(alerts);
        }

        [HttpGet("summary")]
        public async Task<ActionResult<PaymentAlertSummaryResponse>> GetSummary()
        {
            var openCount = await _dbContext.TenantPaymentAlerts.AsNoTracking()
                .CountAsync(a => a.Status == PaymentAlertStatus.Open);

            return Ok(new PaymentAlertSummaryResponse(openCount));
        }

        [HttpPost("{id:guid}/acknowledge")]
        [RequirePermission("ADMIN.PAYMENT_ALERTS.MANAGE")]
        public async Task<IActionResult> Acknowledge(Guid id)
        {
            var alert = await _dbContext.TenantPaymentAlerts.FirstOrDefaultAsync(a => a.TenantPaymentAlertId == id);
            if (alert == null) return NotFound();
            if (alert.Status != PaymentAlertStatus.Open) return BadRequest(new { message = "Only open alerts can be acknowledged." });

            var actorId = _userContext.UserId!.Value;
            alert.Status = PaymentAlertStatus.Acknowledged;
            alert.AcknowledgedAt = DateTime.UtcNow;
            alert.AcknowledgedByUserId = actorId;

            await _dbContext.SaveChangesAsync();
            await _auditLogger.LogAsync(actorId, "PaymentAlertAcknowledged", "TenantPaymentAlert", id.ToString(), null);
            return NoContent();
        }

        [HttpPost("{id:guid}/resolve")]
        [RequirePermission("ADMIN.PAYMENT_ALERTS.MANAGE")]
        public async Task<IActionResult> Resolve(Guid id)
        {
            var alert = await _dbContext.TenantPaymentAlerts.FirstOrDefaultAsync(a => a.TenantPaymentAlertId == id);
            if (alert == null) return NotFound();
            if (alert.Status == PaymentAlertStatus.Resolved) return BadRequest(new { message = "Alert is already resolved." });

            var actorId = _userContext.UserId!.Value;
            alert.Status = PaymentAlertStatus.Resolved;
            alert.ResolvedAt = DateTime.UtcNow;
            alert.ResolvedByUserId = actorId;

            await _dbContext.SaveChangesAsync();
            await _auditLogger.LogAsync(actorId, "PaymentAlertResolved", "TenantPaymentAlert", id.ToString(), null);
            return NoContent();
        }
    }
}
