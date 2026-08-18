using finrecon360_backend.Authorization;
using finrecon360_backend.Data;
using finrecon360_backend.Dtos;
using finrecon360_backend.Dtos.Admin;
using finrecon360_backend.Models;
using finrecon360_backend.Services;
using finrecon360_backend.Services.Export;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace finrecon360_backend.Controllers.Admin
{
    [ApiController]
    [Route("api/admin/audit-logs")]
    [Authorize]
    [RequirePermission("ADMIN.AUDIT_LOGS.VIEW")]
    [EnableRateLimiting("admin")]
    public class AdminTenantAuditLogsController : ControllerBase
    {
        private const int MaxPageSize = 100;

        private readonly AppDbContext _dbContext;
        private readonly ITenantContext _tenantContext;
        private readonly IUserContext _userContext;
        private readonly IReportExporter _reportExporter;

        public AdminTenantAuditLogsController(
            AppDbContext dbContext,
            ITenantContext tenantContext,
            IUserContext userContext,
            IReportExporter reportExporter)
        {
            _dbContext = dbContext;
            _tenantContext = tenantContext;
            _userContext = userContext;
            _reportExporter = reportExporter;
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
            var auth = await AuthorizeTenantAdminAsync();
            if (auth.Error != null) return auth.Error;

            page = page < 1 ? 1 : page;
            pageSize = pageSize < 1 ? 25 : Math.Min(pageSize, MaxPageSize);

            var query = BuildFilteredQuery(auth.TenantId, action, entity, userId, fromUtc, toUtc, search);

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

        [HttpGet("export")]
        public async Task<IActionResult> ExportAuditLogs(
            [FromQuery] string? format,
            [FromQuery] string? action = null,
            [FromQuery] string? entity = null,
            [FromQuery] Guid? userId = null,
            [FromQuery] DateTime? fromUtc = null,
            [FromQuery] DateTime? toUtc = null,
            [FromQuery] string? search = null)
        {
            var auth = await AuthorizeTenantAdminAsync();
            if (auth.Error != null) return auth.Error;

            if (!_reportExporter.TryParseFormat(format, out var exportFormat))
            {
                return BadRequest(new { message = "Unsupported export format. Use 'csv' or 'xlsx'." });
            }

            var query = BuildFilteredQuery(auth.TenantId, action, entity, userId, fromUtc, toUtc, search);

            var totalCount = await query.CountAsync();
            if (totalCount > _reportExporter.MaxRows)
            {
                return BadRequest(new
                {
                    message = $"Export limited to {_reportExporter.MaxRows} rows. Narrow the date range or filters and try again."
                });
            }

            var entities = await query
                .OrderByDescending(a => a.CreatedAt)
                .ToListAsync();
            var items = entities.Select(ToSummaryDto).ToList();

            var file = _reportExporter.Export(items, AuditLogExportColumns.Columns, "Audit Logs", exportFormat);
            return File(file.Content, file.ContentType, $"audit-logs-{DateTime.UtcNow:yyyyMMddHHmmss}.{file.FileExtension}");
        }

        private IQueryable<AuditLog> BuildFilteredQuery(
            Guid tenantId,
            string? action,
            string? entity,
            Guid? userId,
            DateTime? fromUtc,
            DateTime? toUtc,
            string? search)
        {
            var query = from log in _dbContext.AuditLogs.AsNoTracking().Include(a => a.User)
                        join tu in _dbContext.TenantUsers.AsNoTracking()
                            on log.UserId equals tu.UserId
                        where tu.TenantId == tenantId
                        select log;

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

            return query;
        }

        private static AuditLogSummaryDto ToSummaryDto(AuditLog a) => new(
            a.AuditLogId,
            a.UserId,
            a.Action,
            a.Entity,
            a.EntityId,
            a.Metadata,
            a.CreatedAt,
            a.User != null ? a.User.Email : null,
            a.User != null ? a.User.DisplayName : null);

        private async Task<(Guid TenantId, ActionResult? Error)> AuthorizeTenantAdminAsync()
        {
            if (_userContext.UserId is not { } userId)
            {
                return (Guid.Empty, Unauthorized());
            }

            var tenant = await _tenantContext.ResolveAsync();
            if (tenant == null || tenant.Status != TenantStatus.Active)
            {
                return (Guid.Empty, Forbid());
            }

            var tenantMembership = await _dbContext.TenantUsers
                .AsNoTracking()
                .FirstOrDefaultAsync(tu => tu.TenantId == tenant.TenantId && tu.UserId == userId);

            if (tenantMembership == null || tenantMembership.Role != TenantUserRole.TenantAdmin)
            {
                return (Guid.Empty, Forbid());
            }

            return (tenant.TenantId, null);
        }
    }
}
