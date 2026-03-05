using finrecon360_backend.Data;
using finrecon360_backend.Dtos.Me;
using finrecon360_backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;

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
        private readonly ITenantContext _tenantContext;
        private readonly ITenantDbContextFactory _tenantDbContextFactory;

        public MeController(
            AppDbContext dbContext,
            IUserContext userContext,
            IPermissionService permissionService,
            ITenantContext tenantContext,
            ITenantDbContextFactory tenantDbContextFactory)
        {
            _dbContext = dbContext;
            _userContext = userContext;
            _permissionService = permissionService;
            _tenantContext = tenantContext;
            _tenantDbContextFactory = tenantDbContextFactory;
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

            if (!user.IsActive || user.Status == Models.UserStatus.Suspended || user.Status == Models.UserStatus.Banned)
            {
                return Forbid();
            }

            var displayName = user.DisplayName ?? $"{user.FirstName} {user.LastName}".Trim();
            var tenantResolution = await _tenantContext.ResolveAsync();

            IReadOnlyList<string> roles;
            IReadOnlyList<string> permissions;
            Guid? tenantId = null;
            string? tenantName = null;
            string? tenantStatus = null;

            if (tenantResolution != null)
            {
                tenantId = tenantResolution.TenantId;
                tenantName = tenantResolution.Name;
                tenantStatus = tenantResolution.Status.ToString();
                try
                {
                    await using var tenantDb = await _tenantDbContextFactory.CreateAsync(tenantResolution.TenantId);
                    roles = await tenantDb.UserRoles
                        .AsNoTracking()
                        .Where(ur => ur.UserId == userId)
                        .Select(ur => ur.Role.Code)
                        .Distinct()
                        .ToListAsync();

                    permissions = await tenantDb.UserRoles
                        .AsNoTracking()
                        .Where(ur => ur.UserId == userId)
                        .SelectMany(ur => ur.Role.RolePermissions.Select(rp => rp.Permission.Code))
                        .Distinct()
                        .ToListAsync();
                }
                catch (InvalidOperationException)
                {
                    roles = (await _permissionService.GetRolesForUserAsync(userId)).ToList();
                    permissions = (await _permissionService.GetPermissionsForUserAsync(userId)).ToList();
                }
            }
            else
            {
                roles = (await _permissionService.GetRolesForUserAsync(userId)).ToList();
                permissions = (await _permissionService.GetPermissionsForUserAsync(userId)).ToList();
            }

            return Ok(new MeResponse(
                user.UserId,
                user.Email,
                displayName,
                user.Status.ToString(),
                tenantId,
                tenantName,
                tenantStatus,
                roles.ToList(),
                permissions.OrderBy(p => p).ToList()));
        }
    }
}
