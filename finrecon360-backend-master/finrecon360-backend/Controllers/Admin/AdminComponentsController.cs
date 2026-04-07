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
    [Route("api/admin/components")]
    [Authorize]
    [EnableRateLimiting("admin")]
    public class AdminComponentsController : ControllerBase
    {
        private const int MaxPageSize = 100;
        private readonly AppDbContext _dbContext;
        private readonly ITenantContext _tenantContext;
        private readonly ITenantDbContextFactory _tenantDbContextFactory;
        private readonly IUserContext _userContext;

        public AdminComponentsController(AppDbContext dbContext, ITenantContext tenantContext, ITenantDbContextFactory tenantDbContextFactory, IUserContext userContext)
        {
            _dbContext = dbContext;
            _tenantContext = tenantContext;
            _tenantDbContextFactory = tenantDbContextFactory;
            _userContext = userContext;
        }

        [HttpGet]
        [RequirePermission("ADMIN.COMPONENTS.VIEW")]
        public async Task<ActionResult<PagedResult<ComponentSummaryDto>>> GetComponents([FromQuery] int page = 1, [FromQuery] int pageSize = 50, [FromQuery] string? search = null)
        {
            var auth = await AuthorizeTenantAdminAsync();
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            page = page < 1 ? 1 : page;
            pageSize = pageSize is < 1 ? 50 : Math.Min(pageSize, MaxPageSize);

            var query = tenantDb.Components.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                if (term.Length > 100) term = term[..100];
                query = query.Where(c => c.Code.Contains(term) || c.Name.Contains(term));
            }

            var totalCount = await query.CountAsync();
            var items = await query.OrderBy(c => c.Code).Skip((page - 1) * pageSize).Take(pageSize)
                .Select(c => new ComponentSummaryDto(c.ComponentId, c.Code, c.Name, c.RoutePath, c.Category, c.Description, c.IsActive))
                .ToListAsync();

            return Ok(new PagedResult<ComponentSummaryDto> { Items = items, TotalCount = totalCount, Page = page, PageSize = pageSize });
        }

        [HttpGet("{componentId:guid}")]
        [RequirePermission("ADMIN.COMPONENTS.VIEW")]
        public async Task<ActionResult<ComponentSummaryDto>> GetComponent(Guid componentId)
        {
            var auth = await AuthorizeTenantAdminAsync();
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            var component = await tenantDb.Components.AsNoTracking().FirstOrDefaultAsync(c => c.ComponentId == componentId);
            if (component is null) return NotFound();

            return Ok(new ComponentSummaryDto(component.ComponentId, component.Code, component.Name, component.RoutePath, component.Category, component.Description, component.IsActive));
        }

        [HttpPost]
        [RequirePermission("ADMIN.COMPONENTS.CREATE")]
        public async Task<ActionResult<ComponentSummaryDto>> CreateComponent([FromBody] ComponentCreateRequest request)
        {
            var auth = await AuthorizeTenantAdminAsync();
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            var code = request.Code.Trim().ToUpperInvariant();
            var name = request.Name.Trim();
            var routePath = request.RoutePath.Trim();
            var category = string.IsNullOrWhiteSpace(request.Category) ? null : request.Category.Trim();
            var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();

            var duplicate = await tenantDb.Components.AnyAsync(c => c.Code == code || c.Name == name);
            if (duplicate) return Conflict(new { message = "Component code or name already exists." });

            var component = new TenantComponent
            {
                ComponentId = Guid.NewGuid(),
                Code = code,
                Name = name,
                RoutePath = routePath,
                Category = category,
                Description = description,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            tenantDb.Components.Add(component);
            await tenantDb.SaveChangesAsync();

            return CreatedAtAction(nameof(GetComponent), new { componentId = component.ComponentId },
                new ComponentSummaryDto(component.ComponentId, component.Code, component.Name, component.RoutePath, component.Category, component.Description, component.IsActive));
        }

        [HttpPut("{componentId:guid}")]
        [RequirePermission("ADMIN.COMPONENTS.EDIT")]
        public async Task<ActionResult<ComponentSummaryDto>> UpdateComponent(Guid componentId, [FromBody] ComponentUpdateRequest request)
        {
            var auth = await AuthorizeTenantAdminAsync();
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            var component = await tenantDb.Components.FirstOrDefaultAsync(c => c.ComponentId == componentId);
            if (component is null) return NotFound();

            var code = request.Code.Trim().ToUpperInvariant();
            var name = request.Name.Trim();
            var routePath = request.RoutePath.Trim();
            var category = string.IsNullOrWhiteSpace(request.Category) ? null : request.Category.Trim();
            var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();

            var duplicate = await tenantDb.Components.AnyAsync(c => c.ComponentId != componentId && (c.Code == code || c.Name == name));
            if (duplicate) return Conflict(new { message = "Component code or name already exists." });

            component.Code = code;
            component.Name = name;
            component.RoutePath = routePath;
            component.Category = category;
            component.Description = description;

            await tenantDb.SaveChangesAsync();

            return Ok(new ComponentSummaryDto(component.ComponentId, component.Code, component.Name, component.RoutePath, component.Category, component.Description, component.IsActive));
        }

        [HttpPost("{componentId:guid}/deactivate")]
        [RequirePermission("ADMIN.COMPONENTS.DELETE")]
        public async Task<IActionResult> DeactivateComponent(Guid componentId)
        {
            var auth = await AuthorizeTenantAdminAsync();
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            var component = await tenantDb.Components.FirstOrDefaultAsync(c => c.ComponentId == componentId);
            if (component is null) return NotFound();
            component.IsActive = false;
            await tenantDb.SaveChangesAsync();
            return NoContent();
        }

        [HttpPost("{componentId:guid}/activate")]
        [RequirePermission("ADMIN.COMPONENTS.DELETE")]
        public async Task<IActionResult> ActivateComponent(Guid componentId)
        {
            var auth = await AuthorizeTenantAdminAsync();
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            var component = await tenantDb.Components.FirstOrDefaultAsync(c => c.ComponentId == componentId);
            if (component is null) return NotFound();
            component.IsActive = true;
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
