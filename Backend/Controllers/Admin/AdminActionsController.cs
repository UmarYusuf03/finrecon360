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
    [Route("api/admin/actions")]
    [Authorize]
    [RequirePermission("PERMISSION_MANAGEMENT")]
    [EnableRateLimiting("admin")]
    public class AdminActionsController : ControllerBase
    {
        private const int MaxPageSize = 100;
        private readonly AppDbContext _dbContext;

        public AdminActionsController(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        [HttpGet]
        public async Task<ActionResult<PagedResult<ActionSummaryDto>>> GetActions([FromQuery] int page = 1, [FromQuery] int pageSize = 50, [FromQuery] string? search = null)
        {
            page = page < 1 ? 1 : page;
            pageSize = pageSize is < 1 ? 50 : Math.Min(pageSize, MaxPageSize);

            var query = _dbContext.PermissionActions.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                if (term.Length > 50)
                {
                    term = term.Substring(0, 50);
                }
                query = query.Where(a => a.Code.Contains(term) || a.Name.Contains(term));
            }

            var totalCount = await query.CountAsync();
            var items = await query
                .OrderBy(a => a.Code)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(a => new ActionSummaryDto(
                    a.PermissionActionId,
                    a.Code,
                    a.Name,
                    a.Description,
                    a.IsActive))
                .ToListAsync();

            return Ok(new PagedResult<ActionSummaryDto>
            {
                Items = items,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            });
        }

        [HttpGet("{actionId:guid}")]
        public async Task<ActionResult<ActionSummaryDto>> GetAction(Guid actionId)
        {
            var action = await _dbContext.PermissionActions.AsNoTracking()
                .FirstOrDefaultAsync(a => a.PermissionActionId == actionId);
            if (action is null)
            {
                return NotFound();
            }

            return Ok(new ActionSummaryDto(
                action.PermissionActionId,
                action.Code,
                action.Name,
                action.Description,
                action.IsActive));
        }

        [HttpPost]
        public async Task<ActionResult<ActionSummaryDto>> CreateAction([FromBody] ActionCreateRequest request)
        {
            var code = request.Code.Trim().ToUpperInvariant();
            var name = request.Name.Trim();
            var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();

            var duplicate = await _dbContext.PermissionActions.AnyAsync(a => a.Code == code || a.Name == name);
            if (duplicate)
            {
                return Conflict(new { message = "Action code or name already exists." });
            }

            var action = new PermissionAction
            {
                PermissionActionId = Guid.NewGuid(),
                Code = code,
                Name = name,
                Description = description,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.PermissionActions.Add(action);
            await _dbContext.SaveChangesAsync();

            return CreatedAtAction(nameof(GetAction), new { actionId = action.PermissionActionId }, new ActionSummaryDto(
                action.PermissionActionId,
                action.Code,
                action.Name,
                action.Description,
                action.IsActive));
        }

        [HttpPut("{actionId:guid}")]
        public async Task<ActionResult<ActionSummaryDto>> UpdateAction(Guid actionId, [FromBody] ActionUpdateRequest request)
        {
            var action = await _dbContext.PermissionActions.FirstOrDefaultAsync(a => a.PermissionActionId == actionId);
            if (action is null)
            {
                return NotFound();
            }

            var code = request.Code.Trim().ToUpperInvariant();
            var name = request.Name.Trim();
            var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();

            var duplicate = await _dbContext.PermissionActions
                .AnyAsync(a => a.PermissionActionId != actionId && (a.Code == code || a.Name == name));
            if (duplicate)
            {
                return Conflict(new { message = "Action code or name already exists." });
            }

            action.Code = code;
            action.Name = name;
            action.Description = description;

            await _dbContext.SaveChangesAsync();

            return Ok(new ActionSummaryDto(
                action.PermissionActionId,
                action.Code,
                action.Name,
                action.Description,
                action.IsActive));
        }

        [HttpPost("{actionId:guid}/deactivate")]
        public async Task<IActionResult> DeactivateAction(Guid actionId)
        {
            var action = await _dbContext.PermissionActions.FirstOrDefaultAsync(a => a.PermissionActionId == actionId);
            if (action is null)
            {
                return NotFound();
            }

            action.IsActive = false;
            await _dbContext.SaveChangesAsync();
            return NoContent();
        }

        [HttpPost("{actionId:guid}/activate")]
        public async Task<IActionResult> ActivateAction(Guid actionId)
        {
            var action = await _dbContext.PermissionActions.FirstOrDefaultAsync(a => a.PermissionActionId == actionId);
            if (action is null)
            {
                return NotFound();
            }

            action.IsActive = true;
            await _dbContext.SaveChangesAsync();
            return NoContent();
        }
    }
}
