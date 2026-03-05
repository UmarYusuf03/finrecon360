using finrecon360_backend.Authorization;
using finrecon360_backend.Data;
using finrecon360_backend.Dtos.Admin;
using finrecon360_backend.Dtos;
using finrecon360_backend.Models;
using finrecon360_backend.Options;
using finrecon360_backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace finrecon360_backend.Controllers.Admin
{
    [ApiController]
    [Route("api/admin/tenant-registrations")]
    [Authorize]
    [RequirePermission("ADMIN.TENANT_REGISTRATIONS.MANAGE")]
    [EnableRateLimiting("admin")]
    public class AdminTenantRegistrationsController : ControllerBase
    {
        private readonly AppDbContext _dbContext;
        private readonly IUserContext _userContext;
        private readonly IAuditLogger _auditLogger;
        private readonly ITenantProvisioner _tenantProvisioner;
        private readonly ITenantDbProtector _tenantDbProtector;
        private readonly IOnboardingMagicLinkService _magicLinkService;
        private readonly IEmailSender _emailSender;
        private readonly BrevoOptions _brevoOptions;
        private readonly MagicLinkOptions _magicLinkOptions;
        private readonly IPasswordHasher _passwordHasher;
        private readonly ITenantUserDirectoryService _tenantUserDirectoryService;

        public AdminTenantRegistrationsController(
            AppDbContext dbContext,
            IUserContext userContext,
            IAuditLogger auditLogger,
            ITenantProvisioner tenantProvisioner,
            ITenantDbProtector tenantDbProtector,
            IOnboardingMagicLinkService magicLinkService,
            IEmailSender emailSender,
            IOptions<BrevoOptions> brevoOptions,
            IOptions<MagicLinkOptions> magicLinkOptions,
            IPasswordHasher passwordHasher,
            ITenantUserDirectoryService tenantUserDirectoryService)
        {
            _dbContext = dbContext;
            _userContext = userContext;
            _auditLogger = auditLogger;
            _tenantProvisioner = tenantProvisioner;
            _tenantDbProtector = tenantDbProtector;
            _magicLinkService = magicLinkService;
            _emailSender = emailSender;
            _brevoOptions = brevoOptions.Value;
            _magicLinkOptions = magicLinkOptions.Value;
            _passwordHasher = passwordHasher;
            _tenantUserDirectoryService = tenantUserDirectoryService;
        }

        [HttpGet]
        public async Task<ActionResult<PagedResult<TenantRegistrationSummaryDto>>> GetRequests(
            [FromQuery] string? status = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string? search = null)
        {
            page = page < 1 ? 1 : page;
            pageSize = pageSize < 1 ? 20 : Math.Min(pageSize, 100);

            var query = _dbContext.TenantRegistrationRequests.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(r => r.Status == status);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(r => r.BusinessName.Contains(term) || r.AdminEmail.Contains(term));
            }

            var totalCount = await query.CountAsync();
            var items = await query
                .OrderByDescending(r => r.SubmittedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(r => new TenantRegistrationSummaryDto(
                    r.TenantRegistrationRequestId,
                    r.BusinessName,
                    r.AdminEmail,
                    r.PhoneNumber,
                    r.BusinessRegistrationNumber,
                    r.BusinessType,
                    r.Status,
                    r.SubmittedAt))
                .ToListAsync();

            return Ok(new PagedResult<TenantRegistrationSummaryDto>
            {
                Items = items,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            });
        }

        [HttpGet("{requestId:guid}")]
        public async Task<ActionResult<TenantRegistrationDetailDto>> GetRequest(Guid requestId)
        {
            var request = await _dbContext.TenantRegistrationRequests
                .AsNoTracking()
                .Include(r => r.ReviewedByUser)
                .FirstOrDefaultAsync(r => r.TenantRegistrationRequestId == requestId);

            if (request is null)
            {
                return NotFound();
            }

            return Ok(new TenantRegistrationDetailDto(
                request.TenantRegistrationRequestId,
                request.BusinessName,
                request.AdminEmail,
                request.PhoneNumber,
                request.BusinessRegistrationNumber,
                request.BusinessType,
                request.Status,
                request.SubmittedAt,
                request.ReviewedAt,
                request.ReviewedByUser?.Email,
                request.ReviewNote,
                request.OnboardingMetadata));
        }

        [HttpPost("{requestId:guid}/approve")]
        public async Task<IActionResult> Approve(Guid requestId, [FromBody] TenantRegistrationReviewRequest request)
        {
            if (_userContext.UserId is not { } reviewerId)
            {
                return Unauthorized();
            }

            var registration = await _dbContext.TenantRegistrationRequests
                .FirstOrDefaultAsync(r => r.TenantRegistrationRequestId == requestId);

            if (registration == null)
            {
                return NotFound();
            }

            if (registration.Status != "PENDING_REVIEW")
            {
                return BadRequest(new { message = "Only pending requests can be approved." });
            }

            var tenant = new Tenant
            {
                TenantId = Guid.NewGuid(),
                Name = registration.BusinessName,
                Status = TenantStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };

            var provisionResult = await _tenantProvisioner.ProvisionAsync(tenant);
            if (!provisionResult.Success || string.IsNullOrWhiteSpace(provisionResult.ConnectionString))
            {
                return StatusCode(500, new { message = provisionResult.Error ?? "Tenant provisioning failed." });
            }

            var encryptedConnection = _tenantDbProtector.Protect(provisionResult.ConnectionString);

            var tenantDatabase = new TenantDatabase
            {
                TenantDatabaseId = Guid.NewGuid(),
                TenantId = tenant.TenantId,
                EncryptedConnectionString = encryptedConnection,
                Provider = "SqlServer",
                Status = TenantDatabaseStatus.Ready,
                CreatedAt = DateTime.UtcNow,
                ProvisionedAt = DateTime.UtcNow
            };

            var adminEmail = registration.AdminEmail.Trim().ToLowerInvariant();
            var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == adminEmail);
            if (user == null)
            {
                user = new User
                {
                    UserId = Guid.NewGuid(),
                    Email = adminEmail,
                    DisplayName = adminEmail,
                    FirstName = adminEmail,
                    LastName = string.Empty,
                    Country = string.Empty,
                    Gender = string.Empty,
                    PasswordHash = _passwordHasher.Hash(Guid.NewGuid().ToString()),
                    CreatedAt = DateTime.UtcNow,
                    EmailConfirmed = false,
                    IsActive = true,
                    Status = UserStatus.Invited
                };
                _dbContext.Users.Add(user);
            }
            else if (user.Status == UserStatus.Banned)
            {
                return Conflict(new { message = "Admin email belongs to a banned user." });
            }
            else
            {
                var hasAnyTenantMembership = await _dbContext.TenantUsers.AsNoTracking()
                    .AnyAsync(tu => tu.UserId == user.UserId);
                if (hasAnyTenantMembership)
                {
                    return Conflict(new { message = "Admin email is already assigned to another tenant." });
                }
            }

            var tenantUser = new TenantUser
            {
                TenantUserId = Guid.NewGuid(),
                TenantId = tenant.TenantId,
                UserId = user.UserId,
                Role = TenantUserRole.TenantAdmin,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.Tenants.Add(tenant);
            _dbContext.TenantDatabases.Add(tenantDatabase);
            _dbContext.TenantUsers.Add(tenantUser);

            registration.Status = "APPROVED";
            registration.ReviewedAt = DateTime.UtcNow;
            registration.ReviewedByUserId = reviewerId;
            registration.ReviewNote = request.Note;

            await _dbContext.SaveChangesAsync();
            await _tenantUserDirectoryService.UpsertTenantUserAsync(tenant.TenantId, user, TenantUserRole.TenantAdmin);

            var magicLink = await _magicLinkService.CreateTokenAsync(user.UserId, MagicLinkPurpose.TenantOnboarding, HttpContext.Connection.RemoteIpAddress?.ToString());
            if (magicLink != null)
            {
                await SendMagicLinkEmailAsync(user.Email, magicLink.Token);
            }

            await _auditLogger.LogAsync(reviewerId, "TenantRegistrationApproved", "TenantRegistrationRequest", registration.TenantRegistrationRequestId.ToString(), null);
            await _auditLogger.LogAsync(reviewerId, "TenantProvisioned", "Tenant", tenant.TenantId.ToString(), null);

            return NoContent();
        }

        [HttpPost("{requestId:guid}/reject")]
        public async Task<IActionResult> Reject(Guid requestId, [FromBody] TenantRegistrationReviewRequest request)
        {
            if (_userContext.UserId is not { } reviewerId)
            {
                return Unauthorized();
            }

            var registration = await _dbContext.TenantRegistrationRequests
                .FirstOrDefaultAsync(r => r.TenantRegistrationRequestId == requestId);

            if (registration == null)
            {
                return NotFound();
            }

            if (registration.Status != "PENDING_REVIEW")
            {
                return BadRequest(new { message = "Only pending requests can be rejected." });
            }

            registration.Status = "REJECTED";
            registration.ReviewedAt = DateTime.UtcNow;
            registration.ReviewedByUserId = reviewerId;
            registration.ReviewNote = request.Note;

            await _dbContext.SaveChangesAsync();
            await _auditLogger.LogAsync(reviewerId, "TenantRegistrationRejected", "TenantRegistrationRequest", registration.TenantRegistrationRequestId.ToString(), null);

            return NoContent();
        }

        private async Task SendMagicLinkEmailAsync(string email, string token)
        {
            if (string.IsNullOrWhiteSpace(_magicLinkOptions.FrontendBaseUrl))
            {
                throw new InvalidOperationException("FRONTEND_BASE_URL is not configured.");
            }

            var templateId = _brevoOptions.TemplateIdMagicLinkInvite ?? _brevoOptions.TemplateIdMagicLinkVerify;
            if (templateId <= 0)
            {
                throw new InvalidOperationException("Brevo invite template id is not configured.");
            }

            var baseUrl = _magicLinkOptions.FrontendBaseUrl.TrimEnd('/');
            var magicLink = $"{baseUrl}/auth/magic-link?purpose={MagicLinkPurpose.TenantOnboarding}&token={token}";

            var parameters = new Dictionary<string, object>
            {
                ["magicLink"] = magicLink,
                ["expiresInMinutes"] = _magicLinkOptions.ExpiresMinutes
            };

            await _emailSender.SendTemplateAsync(email, templateId, parameters);
        }
    }
}
