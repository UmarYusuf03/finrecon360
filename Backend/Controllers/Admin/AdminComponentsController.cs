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
    [Route("api/admin/components")]
    [Authorize]
    [RequirePermission("ADMIN.COMPONENTS.MANAGE")]
    [EnableRateLimiting("admin")]
    public class AdminComponentsController : ControllerBase
    {
        private const int MaxPageSize = 100;
        private readonly AppDbContext _dbContext;

        public AdminComponentsController(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        [HttpGet]
        public async Task<ActionResult<PagedResult<ComponentSummaryDto>>> GetComponents([FromQuery] int page = 1, [FromQuery] int pageSize = 50, [FromQuery] string? search = null)
        {
            page = page < 1 ? 1 : page;
            pageSize = pageSize is < 1 ? 50 : Math.Min(pageSize, MaxPageSize);

            var query = _dbContext.AppComponents.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                if (term.Length > 100)
                {
                    term = term.Substring(0, 100);
                }
                query = query.Where(c => c.Code.Contains(term) || c.Name.Contains(term));
            }

            var totalCount = await query.CountAsync();
            var items = await query
                .OrderBy(c => c.Code)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(c => new ComponentSummaryDto(
                    c.AppComponentId,
                    c.Code,
                    c.Name,
                    c.RoutePath,
                    c.Category,
                    c.Description,
                    c.IsActive))
                .ToListAsync();

            return Ok(new PagedResult<ComponentSummaryDto>
            {
                Items = items,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            });
        }

        [HttpGet("{componentId:guid}")]
        public async Task<ActionResult<ComponentSummaryDto>> GetComponent(Guid componentId)
        {
            var component = await _dbContext.AppComponents.AsNoTracking()
                .FirstOrDefaultAsync(c => c.AppComponentId == componentId);
            if (component is null)
            {
                return NotFound();
            }

            return Ok(new ComponentSummaryDto(
                component.AppComponentId,
                component.Code,
                component.Name,
                component.RoutePath,
                component.Category,
                component.Description,
                component.IsActive));
        }

        [HttpPost]
        public async Task<ActionResult<ComponentSummaryDto>> CreateComponent([FromBody] ComponentCreateRequest request)
        {
            var code = request.Code.Trim().ToUpperInvariant();
            var name = request.Name.Trim();
            var routePath = request.RoutePath.Trim();
            var category = string.IsNullOrWhiteSpace(request.Category) ? null : request.Category.Trim();
            var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();

            var duplicate = await _dbContext.AppComponents.AnyAsync(c => c.Code == code || c.Name == name);
            if (duplicate)
            {
                return Conflict(new { message = "Component code or name already exists." });
            }

            var component = new AppComponent
            {
                AppComponentId = Guid.NewGuid(),
                Code = code,
                Name = name,
                RoutePath = routePath,
                Category = category,
                Description = description,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.AppComponents.Add(component);
            await _dbContext.SaveChangesAsync();

            return CreatedAtAction(nameof(GetComponent), new { componentId = component.AppComponentId }, new ComponentSummaryDto(
                component.AppComponentId,
                component.Code,
                component.Name,
                component.RoutePath,
                component.Category,
                component.Description,
                component.IsActive));
        }

        [HttpPut("{componentId:guid}")]
        public async Task<ActionResult<ComponentSummaryDto>> UpdateComponent(Guid componentId, [FromBody] ComponentUpdateRequest request)
        {
            var component = await _dbContext.AppComponents.FirstOrDefaultAsync(c => c.AppComponentId == componentId);
            if (component is null)
            {
                return NotFound();
            }

            var code = request.Code.Trim().ToUpperInvariant();
            var name = request.Name.Trim();
            var routePath = request.RoutePath.Trim();
            var category = string.IsNullOrWhiteSpace(request.Category) ? null : request.Category.Trim();
            var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();

            var duplicate = await _dbContext.AppComponents
                .AnyAsync(c => c.AppComponentId != componentId && (c.Code == code || c.Name == name));
            if (duplicate)
            {
                return Conflict(new { message = "Component code or name already exists." });
            }

            component.Code = code;
            component.Name = name;
            component.RoutePath = routePath;
            component.Category = category;
            component.Description = description;

            await _dbContext.SaveChangesAsync();

            return Ok(new ComponentSummaryDto(
                component.AppComponentId,
                component.Code,
                component.Name,
                component.RoutePath,
                component.Category,
                component.Description,
                component.IsActive));
        }

        [HttpPost("{componentId:guid}/deactivate")]
        public async Task<IActionResult> DeactivateComponent(Guid componentId)
        {
            var component = await _dbContext.AppComponents.FirstOrDefaultAsync(c => c.AppComponentId == componentId);
            if (component is null)
            {
                return NotFound();
            }

            component.IsActive = false;
            await _dbContext.SaveChangesAsync();
            return NoContent();
        }

        [HttpPost("{componentId:guid}/activate")]
        public async Task<IActionResult> ActivateComponent(Guid componentId)
        {
            var component = await _dbContext.AppComponents.FirstOrDefaultAsync(c => c.AppComponentId == componentId);
            if (component is null)
            {
                return NotFound();
            }

            component.IsActive = true;
            await _dbContext.SaveChangesAsync();
            return NoContent();
        }
    }
}
