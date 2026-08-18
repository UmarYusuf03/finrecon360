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
    [ApiController]
    [Route("api/system/billing-settings")]
    [Authorize]
    [EnableRateLimiting("admin")]
    public class BillingSettingsController : ControllerBase
    {
        private readonly AppDbContext _dbContext;
        private readonly IUserContext _userContext;
        private readonly IAuditLogger _auditLogger;

        public BillingSettingsController(AppDbContext dbContext, IUserContext userContext, IAuditLogger auditLogger)
        {
            _dbContext = dbContext;
            _userContext = userContext;
            _auditLogger = auditLogger;
        }

        [HttpGet]
        [RequirePermission("ADMIN.PAYMENT_ALERTS.VIEW")]
        public async Task<ActionResult<BillingSettingsResponse>> Get(CancellationToken ct)
        {
            var settings = await GetOrCreateSettingsAsync(ct);
            return Ok(new BillingSettingsResponse(settings.PaymentOverdueSuspensionThresholdDays, settings.UpdatedAt));
        }

        [HttpPut]
        [RequirePermission("ADMIN.PAYMENT_ALERTS.MANAGE")]
        public async Task<ActionResult<BillingSettingsResponse>> Update([FromBody] BillingSettingsUpdateRequest request, CancellationToken ct)
        {
            var settings = await GetOrCreateSettingsAsync(ct);
            var actorId = _userContext.UserId!.Value;

            settings.PaymentOverdueSuspensionThresholdDays = request.PaymentOverdueSuspensionThresholdDays;
            settings.UpdatedAt = DateTime.UtcNow;
            settings.UpdatedByUserId = actorId;

            await _dbContext.SaveChangesAsync(ct);
            await _auditLogger.LogAsync(actorId, "BillingSettingsUpdated", "SystemBillingSettings", settings.SystemBillingSettingsId.ToString(), $"ThresholdDays={request.PaymentOverdueSuspensionThresholdDays}");

            return Ok(new BillingSettingsResponse(settings.PaymentOverdueSuspensionThresholdDays, settings.UpdatedAt));
        }

        private async Task<SystemBillingSettings> GetOrCreateSettingsAsync(CancellationToken ct)
        {
            var settings = await _dbContext.SystemBillingSettings.FirstOrDefaultAsync(ct);
            if (settings != null)
            {
                return settings;
            }

            settings = new SystemBillingSettings
            {
                SystemBillingSettingsId = Guid.NewGuid(),
                PaymentOverdueSuspensionThresholdDays = 7,
                UpdatedAt = DateTime.UtcNow,
            };
            _dbContext.SystemBillingSettings.Add(settings);
            await _dbContext.SaveChangesAsync(ct);
            return settings;
        }
    }
}
