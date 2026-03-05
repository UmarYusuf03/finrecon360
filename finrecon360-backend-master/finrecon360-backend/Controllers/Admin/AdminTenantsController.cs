using finrecon360_backend.Authorization;
using finrecon360_backend.Data;
using finrecon360_backend.Dtos.Admin;
using finrecon360_backend.Dtos;
using finrecon360_backend.Models;
using finrecon360_backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace finrecon360_backend.Controllers.Admin
{
    [ApiController]
    [Route("api/admin/tenants")]
    [Authorize]
    [RequirePermission("ADMIN.TENANTS.MANAGE")]
    [EnableRateLimiting("admin")]
    public class AdminTenantsController : ControllerBase
    {
        private readonly AppDbContext _dbContext;
        private readonly IUserContext _userContext;
        private readonly IAuditLogger _auditLogger;
        private readonly IPasswordHasher _passwordHasher;
        private readonly ITenantUserDirectoryService _tenantUserDirectoryService;
        private readonly ISystemEnforcementService _systemEnforcementService;

        public AdminTenantsController(
            AppDbContext dbContext,
            IUserContext userContext,
            IAuditLogger auditLogger,
            IPasswordHasher passwordHasher,
            ITenantUserDirectoryService tenantUserDirectoryService,
            ISystemEnforcementService systemEnforcementService)
        {
            _dbContext = dbContext;
            _userContext = userContext;
            _auditLogger = auditLogger;
            _passwordHasher = passwordHasher;
            _tenantUserDirectoryService = tenantUserDirectoryService;
            _systemEnforcementService = systemEnforcementService;
        }

        [HttpGet]
        public async Task<ActionResult<PagedResult<TenantSummaryDto>>> GetTenants(
            [FromQuery] string? status = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string? search = null)
        {
            page = page < 1 ? 1 : page;
            pageSize = pageSize < 1 ? 20 : Math.Min(pageSize, 100);

            var query = _dbContext.Tenants.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(t => t.Status.ToString() == status);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(t => t.Name.Contains(term));
            }

            var totalCount = await query.CountAsync();
            var items = await query
                .OrderBy(t => t.Name)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(t => new TenantSummaryDto(
                    t.TenantId,
                    t.Name,
                    t.Status.ToString(),
                    t.CreatedAt,
                    t.CurrentSubscription != null ? t.CurrentSubscription.Plan.Code : null))
                .ToListAsync();

            return Ok(new PagedResult<TenantSummaryDto>
            {
                Items = items,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            });
        }

        [HttpGet("{tenantId:guid}")]
        public async Task<ActionResult<TenantDetailDto>> GetTenant(Guid tenantId)
        {
            var tenant = await _dbContext.Tenants
                .AsNoTracking()
                .Include(t => t.CurrentSubscription)
                .ThenInclude(s => s!.Plan)
                .FirstOrDefaultAsync(t => t.TenantId == tenantId);

            if (tenant is null)
            {
                return NotFound();
            }

            var admins = await _dbContext.TenantUsers
                .AsNoTracking()
                .Where(tu => tu.TenantId == tenantId && tu.Role == TenantUserRole.TenantAdmin)
                .Select(tu => new TenantAdminDto(
                    tu.UserId,
                    tu.User.Email,
                    tu.User.DisplayName ?? $"{tu.User.FirstName} {tu.User.LastName}".Trim(),
                    tu.User.Status.ToString()))
                .ToListAsync();

            TenantSubscriptionDto? subscription = null;
            if (tenant.CurrentSubscription != null)
            {
                subscription = new TenantSubscriptionDto(
                    tenant.CurrentSubscription.SubscriptionId,
                    tenant.CurrentSubscription.Plan.Code,
                    tenant.CurrentSubscription.Plan.Name,
                    tenant.CurrentSubscription.Status.ToString(),
                    tenant.CurrentSubscription.CurrentPeriodStart,
                    tenant.CurrentSubscription.CurrentPeriodEnd);
            }

            return Ok(new TenantDetailDto(
                tenant.TenantId,
                tenant.Name,
                tenant.Status.ToString(),
                tenant.CreatedAt,
                tenant.ActivatedAt,
                tenant.PrimaryDomain,
                subscription,
                admins));
        }

        [HttpPut("{tenantId:guid}/admins")]
        public async Task<IActionResult> ReplaceAdmins(Guid tenantId, [FromBody] TenantAdminSetRequest request)
        {
            if (_userContext.UserId is not { } actorId)
            {
                return Unauthorized();
            }

            var tenant = await _dbContext.Tenants.FirstOrDefaultAsync(t => t.TenantId == tenantId);
            if (tenant is null)
            {
                return NotFound();
            }

            var userIds = new HashSet<Guid>();
            if (request.UserIds != null)
            {
                foreach (var id in request.UserIds)
                {
                    userIds.Add(id);
                }
            }

            if (request.Emails != null)
            {
                foreach (var email in request.Emails)
                {
                    var normalized = email.Trim().ToLowerInvariant();
                    if (string.IsNullOrWhiteSpace(normalized))
                    {
                        continue;
                    }

                    var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == normalized);
                    if (user == null)
                    {
                        user = new User
                        {
                            UserId = Guid.NewGuid(),
                            Email = normalized,
                            DisplayName = normalized,
                            FirstName = normalized,
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
                        return BadRequest(new { message = $"User {normalized} is banned and cannot be added." });
                    }

                    userIds.Add(user.UserId);
                }
            }

            if (request.UserIds != null && request.UserIds.Count > 0)
            {
                var requestedUserIds = request.UserIds.Distinct().ToList();
                var existingUserIds = await _dbContext.Users
                    .AsNoTracking()
                    .Where(u => requestedUserIds.Contains(u.UserId))
                    .Select(u => u.UserId)
                    .ToListAsync();

                if (existingUserIds.Count != requestedUserIds.Count)
                {
                    return BadRequest(new { message = "One or more selected users were not found." });
                }
            }

            if (userIds.Count == 0)
            {
                return BadRequest(new { message = "At least one admin is required." });
            }

            var conflictingEmail = await _dbContext.TenantUsers
                .AsNoTracking()
                .Where(tu => tu.TenantId != tenantId && userIds.Contains(tu.UserId))
                .Select(tu => tu.User.Email)
                .FirstOrDefaultAsync();

            if (!string.IsNullOrWhiteSpace(conflictingEmail))
            {
                return Conflict(new { message = $"Admin email {conflictingEmail} is already assigned to another tenant." });
            }

            var existing = await _dbContext.TenantUsers
                .Where(tu => tu.TenantId == tenantId && tu.Role == TenantUserRole.TenantAdmin)
                .ToListAsync();

            var toRemove = existing.Where(tu => !userIds.Contains(tu.UserId)).ToList();
            if (toRemove.Count > 0)
            {
                _dbContext.TenantUsers.RemoveRange(toRemove);
            }

            var existingIds = existing.Select(tu => tu.UserId).ToHashSet();
            foreach (var userId in userIds)
            {
                if (existingIds.Contains(userId))
                {
                    continue;
                }

                _dbContext.TenantUsers.Add(new TenantUser
                {
                    TenantUserId = Guid.NewGuid(),
                    TenantId = tenantId,
                    UserId = userId,
                    Role = TenantUserRole.TenantAdmin,
                    CreatedAt = DateTime.UtcNow
                });
            }

            await _dbContext.SaveChangesAsync();
            var admins = await _dbContext.Users
                .AsNoTracking()
                .Where(u => userIds.Contains(u.UserId))
                .ToListAsync();
            await _tenantUserDirectoryService.ReplaceTenantAdminsAsync(tenantId, admins);
            await _auditLogger.LogAsync(actorId, "TenantAdminsUpdated", "Tenant", tenantId.ToString(), null);

            return NoContent();
        }

        [HttpPost("{tenantId:guid}/suspend")]
        public async Task<IActionResult> SuspendTenant(Guid tenantId, [FromBody] EnforcementActionRequest request)
        {
            return await ApplyTenantEnforcement(tenantId, TenantStatus.Suspended, EnforcementActionType.Suspend, request);
        }

        [HttpPost("{tenantId:guid}/ban")]
        public async Task<IActionResult> BanTenant(Guid tenantId, [FromBody] EnforcementActionRequest request)
        {
            return await ApplyTenantEnforcement(tenantId, TenantStatus.Banned, EnforcementActionType.Ban, request);
        }

        [HttpPost("{tenantId:guid}/reinstate")]
        public async Task<IActionResult> ReinstateTenant(Guid tenantId)
        {
            var result = await _systemEnforcementService.ReinstateTenantAsync(tenantId, HttpContext.RequestAborted);
            if (result == EnforcementApplyResult.Unauthorized) return Unauthorized();
            if (result == EnforcementApplyResult.NotFound) return NotFound();
            if (result == EnforcementApplyResult.InvalidTarget) return BadRequest(new { message = "Only suspended tenants can be reinstated." });

            var actorId = _userContext.UserId!.Value;
            await _auditLogger.LogAsync(actorId, "TenantReinstated", "Tenant", tenantId.ToString(), null);
            return NoContent();
        }

        private async Task<IActionResult> ApplyTenantEnforcement(Guid tenantId, TenantStatus newStatus, EnforcementActionType actionType, EnforcementActionRequest request)
        {
            var result = await _systemEnforcementService.ApplyTenantActionAsync(tenantId, newStatus, actionType, request, HttpContext.RequestAborted);
            if (result == EnforcementApplyResult.Unauthorized) return Unauthorized();
            if (result == EnforcementApplyResult.NotFound) return NotFound();
            if (result == EnforcementApplyResult.InvalidTarget) return BadRequest(new { message = "Tenant enforcement request is invalid." });

            var actorId = _userContext.UserId!.Value;
            await _auditLogger.LogAsync(actorId, $"Tenant{actionType}", "Tenant", tenantId.ToString(), request.Reason);
            return NoContent();
        }
    }
}
