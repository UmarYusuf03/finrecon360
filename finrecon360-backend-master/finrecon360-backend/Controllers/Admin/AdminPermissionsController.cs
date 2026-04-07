using finrecon360_backend.Data;
using finrecon360_backend.Authorization;
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
    [Route("api/admin/permissions")]
    [Authorize]
    [EnableRateLimiting("admin")]
    public class AdminPermissionsController : ControllerBase
    {
        private const int MaxPageSize = 100;
        private readonly AppDbContext _dbContext;
        private readonly ITenantContext _tenantContext;
        private readonly ITenantDbContextFactory _tenantDbContextFactory;
        private readonly IUserContext _userContext;

        public AdminPermissionsController(AppDbContext dbContext, ITenantContext tenantContext, ITenantDbContextFactory tenantDbContextFactory, IUserContext userContext)
        {
            _dbContext = dbContext;
            _tenantContext = tenantContext;
            _tenantDbContextFactory = tenantDbContextFactory;
            _userContext = userContext;
        }

        [HttpGet]
        [RequirePermission("ADMIN.PERMISSIONS.VIEW")]
        public async Task<ActionResult<PagedResult<PermissionSummaryDto>>> GetPermissions([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? search = null, [FromQuery] string? module = null)
        {
            var auth = await AuthorizeTenantAdminAsync();
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            page = page < 1 ? 1 : page;
            pageSize = pageSize is < 1 ? 20 : Math.Min(pageSize, MaxPageSize);

            var query = tenantDb.Permissions.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(module))
            {
                var moduleTerm = module.Trim();
                if (moduleTerm.Length > 100) moduleTerm = moduleTerm[..100];
                query = query.Where(p => p.Module != null && p.Module.Contains(moduleTerm));
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                if (term.Length > 100) term = term[..100];
                query = query.Where(p => p.Code.Contains(term) || p.Name.Contains(term));
            }

            var totalCount = await query.CountAsync();
            var items = await query.OrderBy(p => p.Code).Skip((page - 1) * pageSize).Take(pageSize)
                .Select(p => new PermissionSummaryDto(p.PermissionId, p.Code, p.Name, p.Description, p.Module))
                .ToListAsync();

            return Ok(new PagedResult<PermissionSummaryDto> { Items = items, TotalCount = totalCount, Page = page, PageSize = pageSize });
        }

        [HttpGet("{permissionId:guid}")]
        [RequirePermission("ADMIN.PERMISSIONS.VIEW")]
        public async Task<ActionResult<PermissionSummaryDto>> GetPermission(Guid permissionId)
        {
            var auth = await AuthorizeTenantAdminAsync();
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            var permission = await tenantDb.Permissions.AsNoTracking().FirstOrDefaultAsync(p => p.PermissionId == permissionId);
            if (permission is null) return NotFound();

            return Ok(new PermissionSummaryDto(permission.PermissionId, permission.Code, permission.Name, permission.Description, permission.Module));
        }

        [HttpPost]
        [RequirePermission("ADMIN.PERMISSIONS.CREATE")]
        public async Task<ActionResult<PermissionSummaryDto>> CreatePermission([FromBody] PermissionCreateRequest request)
        {
            var auth = await AuthorizeTenantAdminAsync();
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            var code = request.Code.Trim().ToUpperInvariant();
            var name = request.Name.Trim();
            var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
            var module = string.IsNullOrWhiteSpace(request.Module) ? null : request.Module.Trim();

            var duplicate = await tenantDb.Permissions.AnyAsync(p => p.Code == code);
            if (duplicate) return Conflict(new { message = "Permission code already exists." });

            var permission = new TenantPermission
            {
                PermissionId = Guid.NewGuid(),
                Code = code,
                Name = name,
                Description = description,
                Module = module,
                CreatedAt = DateTime.UtcNow
            };

            tenantDb.Permissions.Add(permission);
            await tenantDb.SaveChangesAsync();

            return CreatedAtAction(nameof(GetPermission), new { permissionId = permission.PermissionId },
                new PermissionSummaryDto(permission.PermissionId, permission.Code, permission.Name, permission.Description, permission.Module));
        }

        [HttpPut("{permissionId:guid}")]
        [RequirePermission("ADMIN.PERMISSIONS.EDIT")]
        public async Task<ActionResult<PermissionSummaryDto>> UpdatePermission(Guid permissionId, [FromBody] PermissionUpdateRequest request)
        {
            var auth = await AuthorizeTenantAdminAsync();
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            var permission = await tenantDb.Permissions.FirstOrDefaultAsync(p => p.PermissionId == permissionId);
            if (permission is null) return NotFound();

            var code = request.Code.Trim().ToUpperInvariant();
            var name = request.Name.Trim();
            var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
            var module = string.IsNullOrWhiteSpace(request.Module) ? null : request.Module.Trim();

            var duplicate = await tenantDb.Permissions.AnyAsync(p => p.PermissionId != permissionId && p.Code == code);
            if (duplicate) return Conflict(new { message = "Permission code already exists." });

            permission.Code = code;
            permission.Name = name;
            permission.Description = description;
            permission.Module = module;
            await tenantDb.SaveChangesAsync();

            return Ok(new PermissionSummaryDto(permission.PermissionId, permission.Code, permission.Name, permission.Description, permission.Module));
        }

        [HttpDelete("{permissionId:guid}")]
        [RequirePermission("ADMIN.PERMISSIONS.DELETE")]
        public async Task<IActionResult> DeletePermission(Guid permissionId)
        {
            var auth = await AuthorizeTenantAdminAsync();
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            var permission = await tenantDb.Permissions.FirstOrDefaultAsync(p => p.PermissionId == permissionId);
            if (permission is null) return NotFound();

            tenantDb.Permissions.Remove(permission);
            await tenantDb.SaveChangesAsync();
            return NoContent();
        }

        private async Task<(TenantDbContext? Db, ActionResult? Error)> AuthorizeTenantAdminAsync()
        {
            if (_userContext.UserId is not { } userId) return (null, Unauthorized());

            var tenant = await _tenantContext.ResolveAsync();
            if (tenant == null) return (null, Forbid());

            var isTenantMember = await _dbContext.TenantUsers.AsNoTracking()
                .AnyAsync(tu => tu.TenantId == tenant.TenantId && tu.UserId == userId);
            if (!isTenantMember) return (null, Forbid());

            var tenantDb = await _tenantDbContextFactory.CreateAsync(tenant.TenantId);
            var isActiveInTenant = await tenantDb.TenantUsers.AsNoTracking().AnyAsync(tu => tu.UserId == userId && tu.IsActive);
            if (!isActiveInTenant) return (null, Forbid());
            return (tenantDb, null);
        }
    }
}
