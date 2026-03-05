using finrecon360_backend.Data;
using finrecon360_backend.Dtos;
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
    [Route("api/admin/roles")]
    [Authorize]
    [EnableRateLimiting("admin")]
    public class AdminRolesController : ControllerBase
    {
        private const int MaxPageSize = 100;
        private readonly AppDbContext _dbContext;
        private readonly ITenantContext _tenantContext;
        private readonly ITenantDbContextFactory _tenantDbContextFactory;
        private readonly IUserContext _userContext;

        public AdminRolesController(AppDbContext dbContext, ITenantContext tenantContext, ITenantDbContextFactory tenantDbContextFactory, IUserContext userContext)
        {
            _dbContext = dbContext;
            _tenantContext = tenantContext;
            _tenantDbContextFactory = tenantDbContextFactory;
            _userContext = userContext;
        }

        [HttpGet]
        public async Task<ActionResult<PagedResult<RoleSummaryDto>>> GetRoles([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? search = null)
        {
            var auth = await AuthorizeTenantAdminAsync();
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            page = page < 1 ? 1 : page;
            pageSize = pageSize is < 1 ? 20 : Math.Min(pageSize, MaxPageSize);

            var query = tenantDb.Roles.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                if (term.Length > 100) term = term[..100];
                query = query.Where(r => r.Code.Contains(term) || r.Name.Contains(term));
            }

            var totalCount = await query.CountAsync();
            var items = await query.OrderBy(r => r.Name).Skip((page - 1) * pageSize).Take(pageSize)
                .Select(r => new RoleSummaryDto(r.RoleId, r.Code, r.Name, r.Description, r.IsSystem, r.IsActive))
                .ToListAsync();

            return Ok(new PagedResult<RoleSummaryDto> { Items = items, TotalCount = totalCount, Page = page, PageSize = pageSize });
        }

        [HttpGet("{roleId:guid}")]
        public async Task<ActionResult<RoleDetailDto>> GetRole(Guid roleId)
        {
            var auth = await AuthorizeTenantAdminAsync();
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            var role = await tenantDb.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.RoleId == roleId);
            if (role is null) return NotFound();

            var permissions = await tenantDb.RolePermissions.AsNoTracking()
                .Where(rp => rp.RoleId == roleId)
                .OrderBy(rp => rp.Permission.Code)
                .Select(rp => new PermissionSummaryDto(
                    rp.Permission.PermissionId,
                    rp.Permission.Code,
                    rp.Permission.Name,
                    rp.Permission.Description,
                    rp.Permission.Module))
                .ToListAsync();

            return Ok(new RoleDetailDto(role.RoleId, role.Code, role.Name, role.Description, role.IsSystem, role.IsActive, permissions));
        }

        [HttpPost]
        public async Task<ActionResult<RoleSummaryDto>> CreateRole([FromBody] RoleCreateRequest request)
        {
            var auth = await AuthorizeTenantAdminAsync();
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            var code = request.Code.Trim().ToUpperInvariant();
            var name = request.Name.Trim();
            var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();

            var duplicate = await tenantDb.Roles.AnyAsync(r => r.Code == code || r.Name == name);
            if (duplicate) return Conflict(new { message = "Role code or name already exists." });

            var role = new TenantRole
            {
                RoleId = Guid.NewGuid(),
                Code = code,
                Name = name,
                Description = description,
                IsSystem = false,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            tenantDb.Roles.Add(role);
            await tenantDb.SaveChangesAsync();

            return CreatedAtAction(nameof(GetRole), new { roleId = role.RoleId },
                new RoleSummaryDto(role.RoleId, role.Code, role.Name, role.Description, role.IsSystem, role.IsActive));
        }

        [HttpPut("{roleId:guid}")]
        public async Task<ActionResult<RoleSummaryDto>> UpdateRole(Guid roleId, [FromBody] RoleUpdateRequest request)
        {
            var auth = await AuthorizeTenantAdminAsync();
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            var role = await tenantDb.Roles.FirstOrDefaultAsync(r => r.RoleId == roleId);
            if (role is null) return NotFound();

            var code = request.Code.Trim().ToUpperInvariant();
            var name = request.Name.Trim();
            var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();

            if (role.IsSystem && !string.Equals(role.Code, code, StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { message = "System roles cannot change code." });
            }

            var duplicate = await tenantDb.Roles.AnyAsync(r => r.RoleId != roleId && (r.Code == code || r.Name == name));
            if (duplicate) return Conflict(new { message = "Role code or name already exists." });

            role.Code = code;
            role.Name = name;
            role.Description = description;
            await tenantDb.SaveChangesAsync();

            return Ok(new RoleSummaryDto(role.RoleId, role.Code, role.Name, role.Description, role.IsSystem, role.IsActive));
        }

        [HttpDelete("{roleId:guid}")]
        public async Task<IActionResult> DeleteRole(Guid roleId)
        {
            var auth = await AuthorizeTenantAdminAsync();
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            var role = await tenantDb.Roles.FirstOrDefaultAsync(r => r.RoleId == roleId);
            if (role is null) return NotFound();
            if (role.IsSystem) return BadRequest(new { message = "System roles cannot be deleted." });

            var hasUsers = await tenantDb.UserRoles.AnyAsync(ur => ur.RoleId == roleId);
            if (hasUsers) return Conflict(new { message = "Role is assigned to users." });

            tenantDb.Roles.Remove(role);
            await tenantDb.SaveChangesAsync();
            return NoContent();
        }

        [HttpPost("{roleId:guid}/deactivate")]
        public async Task<IActionResult> DeactivateRole(Guid roleId)
        {
            var auth = await AuthorizeTenantAdminAsync();
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            var role = await tenantDb.Roles.FirstOrDefaultAsync(r => r.RoleId == roleId);
            if (role is null) return NotFound();
            if (role.IsSystem) return BadRequest(new { message = "System roles cannot be deactivated." });

            role.IsActive = false;
            await tenantDb.SaveChangesAsync();
            return NoContent();
        }

        [HttpPost("{roleId:guid}/activate")]
        public async Task<IActionResult> ActivateRole(Guid roleId)
        {
            var auth = await AuthorizeTenantAdminAsync();
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            var role = await tenantDb.Roles.FirstOrDefaultAsync(r => r.RoleId == roleId);
            if (role is null) return NotFound();

            role.IsActive = true;
            await tenantDb.SaveChangesAsync();
            return NoContent();
        }

        [HttpPut("{roleId:guid}/permissions")]
        public async Task<IActionResult> ReplaceRolePermissions(Guid roleId, [FromBody] RolePermissionSetRequest request)
        {
            var auth = await AuthorizeTenantAdminAsync();
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            var roleExists = await tenantDb.Roles.AnyAsync(r => r.RoleId == roleId);
            if (!roleExists) return NotFound();

            var (permissionIds, hasMissing) = await ResolvePermissionIdsAsync(tenantDb, request);
            if (hasMissing) return BadRequest(new { message = "One or more permission identifiers were not found." });
            if (permissionIds.Count > 500) return BadRequest(new { message = "Too many permissions in a single request." });

            var existing = await tenantDb.RolePermissions.Where(rp => rp.RoleId == roleId).ToListAsync();
            var toRemove = existing.Where(rp => !permissionIds.Contains(rp.PermissionId)).ToList();
            var existingIds = existing.Select(rp => rp.PermissionId).ToHashSet();
            var toAdd = permissionIds.Where(id => !existingIds.Contains(id)).ToList();

            if (toRemove.Count > 0) tenantDb.RolePermissions.RemoveRange(toRemove);

            foreach (var permissionId in toAdd)
            {
                tenantDb.RolePermissions.Add(new TenantRolePermission
                {
                    RoleId = roleId,
                    PermissionId = permissionId,
                    GrantedAt = DateTime.UtcNow
                });
            }

            await tenantDb.SaveChangesAsync();
            return NoContent();
        }

        [HttpPost("{roleId:guid}/permissions/{permissionId:guid}")]
        public async Task<IActionResult> AddRolePermission(Guid roleId, Guid permissionId)
        {
            var auth = await AuthorizeTenantAdminAsync();
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            var roleExists = await tenantDb.Roles.AnyAsync(r => r.RoleId == roleId);
            if (!roleExists) return NotFound();
            var permissionExists = await tenantDb.Permissions.AnyAsync(p => p.PermissionId == permissionId);
            if (!permissionExists) return NotFound();

            var exists = await tenantDb.RolePermissions.AnyAsync(rp => rp.RoleId == roleId && rp.PermissionId == permissionId);
            if (exists) return NoContent();

            tenantDb.RolePermissions.Add(new TenantRolePermission { RoleId = roleId, PermissionId = permissionId, GrantedAt = DateTime.UtcNow });
            await tenantDb.SaveChangesAsync();
            return NoContent();
        }

        [HttpDelete("{roleId:guid}/permissions/{permissionId:guid}")]
        public async Task<IActionResult> RemoveRolePermission(Guid roleId, Guid permissionId)
        {
            var auth = await AuthorizeTenantAdminAsync();
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            var mapping = await tenantDb.RolePermissions.FirstOrDefaultAsync(rp => rp.RoleId == roleId && rp.PermissionId == permissionId);
            if (mapping is null) return NoContent();

            tenantDb.RolePermissions.Remove(mapping);
            await tenantDb.SaveChangesAsync();
            return NoContent();
        }

        private static async Task<(HashSet<Guid> PermissionIds, bool HasMissing)> ResolvePermissionIdsAsync(TenantDbContext tenantDb, RolePermissionSetRequest request)
        {
            var ids = new HashSet<Guid>();
            var hasMissing = false;

            if (request.PermissionIds is { Count: > 0 })
            {
                var requestedIds = request.PermissionIds.Distinct().ToList();
                var existingIds = await tenantDb.Permissions.Where(p => requestedIds.Contains(p.PermissionId)).Select(p => p.PermissionId).ToListAsync();
                if (existingIds.Count != requestedIds.Count) hasMissing = true;
                foreach (var id in existingIds) ids.Add(id);
            }

            if (request.PermissionCodes is { Count: > 0 })
            {
                var codes = request.PermissionCodes.Select(c => c.Trim().ToUpperInvariant()).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().ToList();
                var matchingIds = await tenantDb.Permissions.Where(p => codes.Contains(p.Code)).Select(p => p.PermissionId).ToListAsync();
                if (matchingIds.Count != codes.Count) hasMissing = true;
                foreach (var id in matchingIds) ids.Add(id);
            }

            return (ids, hasMissing);
        }

        private async Task<(TenantDbContext? Db, ActionResult? Error)> AuthorizeTenantAdminAsync()
        {
            if (_userContext.UserId is not { } userId) return (null, Unauthorized());

            var tenant = await _tenantContext.ResolveAsync();
            if (tenant == null) return (null, Forbid());

            var isTenantAdmin = await _dbContext.TenantUsers.AsNoTracking()
                .AnyAsync(tu => tu.TenantId == tenant.TenantId && tu.UserId == userId && tu.Role == TenantUserRole.TenantAdmin);
            if (!isTenantAdmin) return (null, Forbid());

            var tenantDb = await _tenantDbContextFactory.CreateAsync(tenant.TenantId);
            var isActiveInTenant = await tenantDb.TenantUsers.AsNoTracking().AnyAsync(tu => tu.UserId == userId && tu.IsActive);
            if (!isActiveInTenant) return (null, Forbid());
            return (tenantDb, null);
        }
    }
}
