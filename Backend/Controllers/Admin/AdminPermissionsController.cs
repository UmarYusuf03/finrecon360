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
    [Route("api/admin/permissions")]
    [Authorize]
    [RequirePermission("PERMISSION_MANAGEMENT")]
    [EnableRateLimiting("admin")]
    public class AdminPermissionsController : ControllerBase
    {
        private const int MaxPageSize = 100;
        private readonly AppDbContext _dbContext;

        public AdminPermissionsController(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        [HttpGet]
        public async Task<ActionResult<PagedResult<PermissionSummaryDto>>> GetPermissions(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string? search = null,
            [FromQuery] string? module = null)
        {
            page = page < 1 ? 1 : page;
            pageSize = pageSize is < 1 ? 20 : Math.Min(pageSize, MaxPageSize);

            var query = _dbContext.Permissions.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(module))
            {
                var moduleTerm = module.Trim();
                if (moduleTerm.Length > 100)
                {
                    moduleTerm = moduleTerm.Substring(0, 100);
                }
                query = query.Where(p => p.Module != null && p.Module.Contains(moduleTerm));
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                if (term.Length > 100)
                {
                    term = term.Substring(0, 100);
                }
                query = query.Where(p => p.Code.Contains(term) || p.Name.Contains(term));
            }

            var totalCount = await query.CountAsync();
            var items = await query
                .OrderBy(p => p.Code)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(p => new PermissionSummaryDto(
                    p.PermissionId,
                    p.Code,
                    p.Name,
                    p.Description,
                    p.Module))
                .ToListAsync();

            return Ok(new PagedResult<PermissionSummaryDto>
            {
                Items = items,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            });
        }

        [HttpGet("{permissionId:guid}")]
        public async Task<ActionResult<PermissionSummaryDto>> GetPermission(Guid permissionId)
        {
            var permission = await _dbContext.Permissions.AsNoTracking()
                .FirstOrDefaultAsync(p => p.PermissionId == permissionId);
            if (permission is null)
            {
                return NotFound();
            }

            return Ok(new PermissionSummaryDto(
                permission.PermissionId,
                permission.Code,
                permission.Name,
                permission.Description,
                permission.Module));
        }

        [HttpPost]
        public async Task<ActionResult<PermissionSummaryDto>> CreatePermission([FromBody] PermissionCreateRequest request)
        {
            var code = request.Code.Trim().ToUpperInvariant();
            var name = request.Name.Trim();
            var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
            var module = string.IsNullOrWhiteSpace(request.Module) ? null : request.Module.Trim();

            var duplicate = await _dbContext.Permissions.AnyAsync(p => p.Code == code);
            if (duplicate)
            {
                return Conflict(new { message = "Permission code already exists." });
            }

            var permission = new Permission
            {
                PermissionId = Guid.NewGuid(),
                Code = code,
                Name = name,
                Description = description,
                Module = module,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.Permissions.Add(permission);
            await _dbContext.SaveChangesAsync();

            return CreatedAtAction(nameof(GetPermission), new { permissionId = permission.PermissionId }, new PermissionSummaryDto(
                permission.PermissionId,
                permission.Code,
                permission.Name,
                permission.Description,
                permission.Module));
        }

        [HttpPut("{permissionId:guid}")]
        public async Task<ActionResult<PermissionSummaryDto>> UpdatePermission(Guid permissionId, [FromBody] PermissionUpdateRequest request)
        {
            var permission = await _dbContext.Permissions.FirstOrDefaultAsync(p => p.PermissionId == permissionId);
            if (permission is null)
            {
                return NotFound();
            }

            var code = request.Code.Trim().ToUpperInvariant();
            var name = request.Name.Trim();
            var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
            var module = string.IsNullOrWhiteSpace(request.Module) ? null : request.Module.Trim();

            var duplicate = await _dbContext.Permissions
                .AnyAsync(p => p.PermissionId != permissionId && p.Code == code);
            if (duplicate)
            {
                return Conflict(new { message = "Permission code already exists." });
            }

            permission.Code = code;
            permission.Name = name;
            permission.Description = description;
            permission.Module = module;

            await _dbContext.SaveChangesAsync();

            return Ok(new PermissionSummaryDto(
                permission.PermissionId,
                permission.Code,
                permission.Name,
                permission.Description,
                permission.Module));
        }

        [HttpDelete("{permissionId:guid}")]
        public async Task<IActionResult> DeletePermission(Guid permissionId)
        {
            var permission = await _dbContext.Permissions.FirstOrDefaultAsync(p => p.PermissionId == permissionId);
            if (permission is null)
            {
                return NotFound();
            }

            _dbContext.Permissions.Remove(permission);
            await _dbContext.SaveChangesAsync();
            return NoContent();
        }
    }
}
