using System.Text.Json;
using finrecon360_backend.Data;
using finrecon360_backend.Dtos.Public;
using finrecon360_backend.Models;
using finrecon360_backend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace finrecon360_backend.Controllers.Public
{
    /// <summary>
    /// WHY: Entry point for tenant self-registration. Accepts business details and creates a TenantRegistrationRequest
    /// in PENDING_REVIEW status. This flow intentionally does NOT provision a database or create a tenant immediately;
    /// that happens only after system-admin approval. Prevents spam/resource exhaustion before human review.
    /// Enforces allowed business types to maintain data quality during onboarding.
    /// </summary>
    [ApiController]
    [Route("api/public/tenant-registrations")]
    [EnableRateLimiting("auth-link")]
    public class TenantRegistrationsController : ControllerBase
    {
        private static readonly HashSet<string> AllowedBusinessTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "VEHICLE_RENTAL",
            "ACCOMMODATION"
        };

        private readonly AppDbContext _dbContext;
        private readonly IAuditLogger _auditLogger;

        public TenantRegistrationsController(AppDbContext dbContext, IAuditLogger auditLogger)
        {
            _dbContext = dbContext;
            _auditLogger = auditLogger;
        }

        [HttpPost]
        public async Task<ActionResult<TenantRegistrationCreateResponse>> Create([FromBody] TenantRegistrationCreateRequest request)
        {
            var businessName = request.BusinessName.Trim();
            var adminEmail = request.AdminEmail.Trim().ToLowerInvariant();
            var phoneNumber = request.PhoneNumber.Trim();
            var businessRegistrationNumber = request.BusinessRegistrationNumber.Trim();
            var businessType = request.BusinessType.Trim().ToUpperInvariant();

            if (string.IsNullOrWhiteSpace(businessName) ||
                string.IsNullOrWhiteSpace(adminEmail) ||
                string.IsNullOrWhiteSpace(phoneNumber) ||
                string.IsNullOrWhiteSpace(businessRegistrationNumber) ||
                string.IsNullOrWhiteSpace(businessType))
            {
                return BadRequest(new { message = "Business name, admin email, phone number, business registration number, and business type are required." });
            }

            if (!AllowedBusinessTypes.Contains(businessType))
            {
                return BadRequest(new { message = "Business type must be VEHICLE_RENTAL or ACCOMMODATION." });
            }

            var existing = await _dbContext.TenantRegistrationRequests
                .AsNoTracking()
                .AnyAsync(r => r.AdminEmail == adminEmail && r.Status == "PENDING_REVIEW");

            if (existing)
            {
                return Conflict(new { message = "A pending request already exists for this email." });
            }

            var metadata = request.OnboardingMetadata.HasValue
                ? JsonSerializer.Serialize(request.OnboardingMetadata.Value)
                : null;

            var entity = new TenantRegistrationRequest
            {
                TenantRegistrationRequestId = Guid.NewGuid(),
                BusinessName = businessName,
                AdminEmail = adminEmail,
                PhoneNumber = phoneNumber,
                BusinessRegistrationNumber = businessRegistrationNumber,
                BusinessType = businessType,
                OnboardingMetadata = metadata,
                Status = "PENDING_REVIEW",
                SubmittedAt = DateTime.UtcNow
            };

            _dbContext.TenantRegistrationRequests.Add(entity);
            await _dbContext.SaveChangesAsync();

            await _auditLogger.LogAsync(null, "TenantRegistrationSubmitted", "TenantRegistrationRequest", entity.TenantRegistrationRequestId.ToString(), $"email={adminEmail}");

            return Ok(new TenantRegistrationCreateResponse(entity.TenantRegistrationRequestId, entity.Status));
        }
    }
}
