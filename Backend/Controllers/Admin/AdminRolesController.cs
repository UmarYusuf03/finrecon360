using finrecon360_backend.Authorization;
using finrecon360_backend.Data;
using finrecon360_backend.Dtos;
using finrecon360_backend.Dtos.Admin;
using finrecon360_backend.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace finrecon360_backend.Controllers.Admin
{
    [ApiController]
    [Route("api/admin/roles")]
    [Authorize]
    [RequirePermission("ROLE_MANAGEMENT")]
    [EnableRateLimiting("admin")]
    public class AdminRolesController : ControllerBase
    {
        private const int MaxPageSize = 100;
        private readonly AppDbContext _dbContext;

        public AdminRolesController(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        [HttpGet]
        public async Task<ActionResult<PagedResult<RoleSummaryDto>>> GetRoles([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? search = null)
        {
            page = page < 1 ? 1 : page;
            pageSize = pageSize is < 1 ? 20 : Math.Min(pageSize, MaxPageSize);

            var query = _dbContext.Roles.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                if (term.Length > 100)
                {
                    term = term.Substring(0, 100);
                }

                query = query.Where(r => r.Code.Contains(term) || r.Name.Contains(term));
            }

            var totalCount = await query.CountAsync();
            var items = await query
                .OrderBy(r => r.Name)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(r => new RoleSummaryDto(
                    r.RoleId,
                    r.Code,
                    r.Name,
                    r.Description,
                    r.IsSystem,
                    r.IsActive))
                .ToListAsync();

            return Ok(new PagedResult<RoleSummaryDto>
            {
                Items = items,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            });
        }

        [HttpGet("{roleId:guid}")]
        public async Task<ActionResult<RoleDetailDto>> GetRole(Guid roleId)
        {
            var role = await _dbContext.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.RoleId == roleId);
            if (role is null)
            {
                return NotFound();
            }

            var permissions = await _dbContext.RolePermissions
                .AsNoTracking()
                .Where(rp => rp.RoleId == roleId)
                .Select(rp => new PermissionSummaryDto(
                    rp.Permission.PermissionId,
                    rp.Permission.Code,
                    rp.Permission.Name,
                    rp.Permission.Description,
                    rp.Permission.Module))
                .OrderBy(p => p.Code)
                .ToListAsync();

            return Ok(new RoleDetailDto(
                role.RoleId,
                role.Code,
                role.Name,
                role.Description,
                role.IsSystem,
                role.IsActive,
                permissions));
        }

        [HttpPost]
        public async Task<ActionResult<RoleSummaryDto>> CreateRole([FromBody] RoleCreateRequest request)
        {
            var code = request.Code.Trim().ToUpperInvariant();
            var name = request.Name.Trim();
            var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();

            var duplicate = await _dbContext.Roles.AnyAsync(r => r.Code == code || r.Name == name);
            if (duplicate)
            {
                return Conflict(new { message = "Role code or name already exists." });
            }

            var role = new Role
            {
                RoleId = Guid.NewGuid(),
                Code = code,
                Name = name,
                Description = description,
                IsSystem = false,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.Roles.Add(role);
            await _dbContext.SaveChangesAsync();

            return CreatedAtAction(nameof(GetRole), new { roleId = role.RoleId }, new RoleSummaryDto(
                role.RoleId,
                role.Code,
                role.Name,
                role.Description,
                role.IsSystem,
                role.IsActive));
        }

        [HttpPut("{roleId:guid}")]
        public async Task<ActionResult<RoleSummaryDto>> UpdateRole(Guid roleId, [FromBody] RoleUpdateRequest request)
        {
            var role = await _dbContext.Roles.FirstOrDefaultAsync(r => r.RoleId == roleId);
            if (role is null)
            {
                return NotFound();
            }

            var code = request.Code.Trim().ToUpperInvariant();
            var name = request.Name.Trim();
            var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();

            if (role.IsSystem && !string.Equals(role.Code, code, StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { message = "System roles cannot change code." });
            }

            var duplicate = await _dbContext.Roles
                .AnyAsync(r => r.RoleId != roleId && (r.Code == code || r.Name == name));
            if (duplicate)
            {
                return Conflict(new { message = "Role code or name already exists." });
            }

            role.Code = code;
            role.Name = name;
            role.Description = description;

            await _dbContext.SaveChangesAsync();

            return Ok(new RoleSummaryDto(
                role.RoleId,
                role.Code,
                role.Name,
                role.Description,
                role.IsSystem,
                role.IsActive));
        }

        [HttpDelete("{roleId:guid}")]
        public async Task<IActionResult> DeleteRole(Guid roleId)
        {
            var role = await _dbContext.Roles.FirstOrDefaultAsync(r => r.RoleId == roleId);
            if (role is null)
            {
                return NotFound();
            }

            if (role.IsSystem)
            {
                return BadRequest(new { message = "System roles cannot be deleted." });
            }

            var hasUsers = await _dbContext.UserRoles.AnyAsync(ur => ur.RoleId == roleId);
            if (hasUsers)
            {
                return Conflict(new { message = "Role is assigned to users." });
            }

            _dbContext.Roles.Remove(role);
            await _dbContext.SaveChangesAsync();
            return NoContent();
        }

        [HttpPost("{roleId:guid}/deactivate")]
        public async Task<IActionResult> DeactivateRole(Guid roleId)
        {
            var role = await _dbContext.Roles.FirstOrDefaultAsync(r => r.RoleId == roleId);
            if (role is null)
            {
                return NotFound();
            }

            if (role.IsSystem)
            {
                return BadRequest(new { message = "System roles cannot be deactivated." });
            }

            role.IsActive = false;
            await _dbContext.SaveChangesAsync();
            return NoContent();
        }

        [HttpPost("{roleId:guid}/activate")]
        public async Task<IActionResult> ActivateRole(Guid roleId)
        {
            var role = await _dbContext.Roles.FirstOrDefaultAsync(r => r.RoleId == roleId);
            if (role is null)
            {
                return NotFound();
            }

            role.IsActive = true;
            await _dbContext.SaveChangesAsync();
            return NoContent();
        }

        [HttpPut("{roleId:guid}/permissions")]
        public async Task<IActionResult> ReplaceRolePermissions(Guid roleId, [FromBody] RolePermissionSetRequest request)
        {
            var roleExists = await _dbContext.Roles.AnyAsync(r => r.RoleId == roleId);
            if (!roleExists)
            {
                return NotFound();
            }

            var (permissionIds, hasMissing) = await ResolvePermissionIdsAsync(request);
            if (hasMissing)
            {
                return BadRequest(new { message = "One or more permission identifiers were not found." });
            }

            if (permissionIds.Count > 500)
            {
                return BadRequest(new { message = "Too many permissions in a single request." });
            }

            var existing = await _dbContext.RolePermissions
                .Where(rp => rp.RoleId == roleId)
                .ToListAsync();

            var toRemove = existing.Where(rp => !permissionIds.Contains(rp.PermissionId)).ToList();
            var existingIds = existing.Select(rp => rp.PermissionId).ToHashSet();
            var toAdd = permissionIds.Where(id => !existingIds.Contains(id)).ToList();

            if (toRemove.Count > 0)
            {
                _dbContext.RolePermissions.RemoveRange(toRemove);
            }

            foreach (var permissionId in toAdd)
            {
                _dbContext.RolePermissions.Add(new RolePermission
                {
                    RoleId = roleId,
                    PermissionId = permissionId,
                    GrantedAt = DateTime.UtcNow
                });
            }

            await _dbContext.SaveChangesAsync();
            return NoContent();
        }

        [HttpPost("{roleId:guid}/permissions/{permissionId:guid}")]
        public async Task<IActionResult> AddRolePermission(Guid roleId, Guid permissionId)
        {
            var roleExists = await _dbContext.Roles.AnyAsync(r => r.RoleId == roleId);
            if (!roleExists)
            {
                return NotFound();
            }

            var permissionExists = await _dbContext.Permissions.AnyAsync(p => p.PermissionId == permissionId);
            if (!permissionExists)
            {
                return NotFound();
            }

            var exists = await _dbContext.RolePermissions.AnyAsync(rp => rp.RoleId == roleId && rp.PermissionId == permissionId);
            if (exists)
            {
                return NoContent();
            }

            _dbContext.RolePermissions.Add(new RolePermission
            {
                RoleId = roleId,
                PermissionId = permissionId,
                GrantedAt = DateTime.UtcNow
            });

            await _dbContext.SaveChangesAsync();
            return NoContent();
        }

        [HttpDelete("{roleId:guid}/permissions/{permissionId:guid}")]
        public async Task<IActionResult> RemoveRolePermission(Guid roleId, Guid permissionId)
        {
            var mapping = await _dbContext.RolePermissions
                .FirstOrDefaultAsync(rp => rp.RoleId == roleId && rp.PermissionId == permissionId);
            if (mapping is null)
            {
                return NotFound();
            }

            _dbContext.RolePermissions.Remove(mapping);
            await _dbContext.SaveChangesAsync();
            return NoContent();
        }

        private async Task<(HashSet<Guid> PermissionIds, bool HasMissing)> ResolvePermissionIdsAsync(RolePermissionSetRequest request)
        {
            var ids = new HashSet<Guid>();
            var hasMissing = false;

            if (request.PermissionIds != null)
            {
                var requestedIds = request.PermissionIds.Distinct().ToList();
                var existingIds = await _dbContext.Permissions
                    .Where(p => requestedIds.Contains(p.PermissionId))
                    .Select(p => p.PermissionId)
                    .ToListAsync();

                if (existingIds.Count != requestedIds.Count)
                {
                    hasMissing = true;
                }

                foreach (var id in existingIds)
                {
                    ids.Add(id);
                }
            }

            if (request.PermissionCodes != null && request.PermissionCodes.Count > 0)
            {
                var codes = request.PermissionCodes
                    .Select(c => c.Trim().ToUpperInvariant())
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Distinct()
                    .ToList();

                var permissionMatches = await _dbContext.Permissions
                    .Where(p => codes.Contains(p.Code))
                    .Select(p => new { p.PermissionId, p.Code })
                    .ToListAsync();

                if (permissionMatches.Count != codes.Count)
                {
                    hasMissing = true;
                }

                foreach (var match in permissionMatches)
                {
                    ids.Add(match.PermissionId);
                }
            }

            return (ids, hasMissing);
        }
    }
}
