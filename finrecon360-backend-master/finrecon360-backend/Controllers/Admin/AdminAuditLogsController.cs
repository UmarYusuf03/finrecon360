using finrecon360_backend.Authorization;
using finrecon360_backend.Data;
using finrecon360_backend.Dtos;
using finrecon360_backend.Dtos.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace finrecon360_backend.Controllers.Admin
{
    [ApiController]
    [Route("api/system/audit-logs")]
    [Authorize]
    [RequirePermission("ADMIN.TENANTS.MANAGE")]
    [EnableRateLimiting("admin")]
    public class AdminAuditLogsController : ControllerBase
    {
        private const int MaxPageSize = 100;

        private readonly AppDbContext _dbContext;

        public AdminAuditLogsController(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        [HttpGet]
        public async Task<ActionResult<PagedResult<AuditLogSummaryDto>>> GetAuditLogs(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 25,
            [FromQuery] string? action = null,
            [FromQuery] string? entity = null,
            [FromQuery] Guid? userId = null,
            [FromQuery] DateTime? fromUtc = null,
            [FromQuery] DateTime? toUtc = null,
            [FromQuery] string? search = null)
        {
            var subject = User.FindFirst("sub")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(subject, out var actorId))
            {
                return Unauthorized();
            }

            var isSystemAdmin = await _dbContext.Users
                .AsNoTracking()
                .Where(u => u.UserId == actorId)
                .Select(u => u.IsSystemAdmin)
                .FirstOrDefaultAsync();

            if (!isSystemAdmin)
            {
                return Forbid();
            }

            page = page < 1 ? 1 : page;
            pageSize = pageSize < 1 ? 25 : Math.Min(pageSize, MaxPageSize);

            var query = _dbContext.AuditLogs
                .AsNoTracking()
                .Include(a => a.User)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(action))
            {
                var normalizedAction = action.Trim();
                query = query.Where(a => a.Action.Contains(normalizedAction));
            }

            if (!string.IsNullOrWhiteSpace(entity))
            {
                var normalizedEntity = entity.Trim();
                query = query.Where(a => a.Entity != null && a.Entity.Contains(normalizedEntity));
            }

            if (userId.HasValue)
            {
                query = query.Where(a => a.UserId == userId.Value);
            }

            if (fromUtc.HasValue)
            {
                query = query.Where(a => a.CreatedAt >= fromUtc.Value);
            }

            if (toUtc.HasValue)
            {
                query = query.Where(a => a.CreatedAt <= toUtc.Value);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(a =>
                    a.Action.Contains(term) ||
                    (a.Entity != null && a.Entity.Contains(term)) ||
                    (a.Metadata != null && a.Metadata.Contains(term)) ||
                    (a.User != null && (
                        a.User.Email.Contains(term) ||
                        (a.User.DisplayName != null && a.User.DisplayName.Contains(term)))));
            }

            var totalCount = await query.CountAsync();
            var items = await query
                .OrderByDescending(a => a.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(a => new AuditLogSummaryDto(
                    a.AuditLogId,
                    a.UserId,
                    a.Action,
                    a.Entity,
                    a.EntityId,
                    a.Metadata,
                    a.CreatedAt,
                    a.User != null ? a.User.Email : null,
                    a.User != null ? a.User.DisplayName : null))
                .ToListAsync();

            return Ok(new PagedResult<AuditLogSummaryDto>
            {
                Items = items,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            });
        }
    }
}
