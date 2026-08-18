using finrecon360_backend.Authorization;
using finrecon360_backend.Data;
using finrecon360_backend.Dtos.Reporting;
using finrecon360_backend.Services;
using finrecon360_backend.Services.Export;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace finrecon360_backend.Controllers.Admin
{
    /// <summary>
    /// Reads from ReconciliationDailySnapshot only — cheap, already-aggregated queries, no
    /// scanning of ReconciliationMatchGroup/ReconciliationEvent at read time. Snapshot rows are
    /// populated once daily by ReconciliationSnapshotHostedService, so today (and any day before
    /// the first snapshot ran) will show no data yet — this is expected, not a bug.
    /// </summary>
    [ApiController]
    [Route("api/admin/reconciliation-reports")]
    [Authorize]
    [RequirePermission("ADMIN.RECONCILIATION.VIEW")]
    public class ReconciliationReportsController : ControllerBase
    {
        private const int MaxTrendDays = 366;

        private static readonly IReadOnlyList<ExportColumn<ReconciliationTrendDayDto>> TrendExportColumns = new List<ExportColumn<ReconciliationTrendDayDto>>
        {
            new("Date", d => d.SnapshotDate.ToString("yyyy-MM-dd")),
            new("Match Level", d => d.MatchLevel),
            new("Matched", d => d.MatchedCount.ToString()),
            new("Confirmed", d => d.ConfirmedCount.ToString()),
            new("Exceptions", d => d.ExceptionCount.ToString()),
            new("Unmatched", d => d.UnmatchedCount.ToString()),
            new("Avg Time To Match (hrs)", d => d.AverageTimeToMatchHours?.ToString("0.00")),
        };

        private readonly AppDbContext _dbContext;
        private readonly ITenantContext _tenantContext;
        private readonly ITenantDbContextFactory _tenantDbContextFactory;
        private readonly IUserContext _userContext;
        private readonly IReportExporter _reportExporter;

        public ReconciliationReportsController(
            AppDbContext dbContext,
            ITenantContext tenantContext,
            ITenantDbContextFactory tenantDbContextFactory,
            IUserContext userContext,
            IReportExporter reportExporter)
        {
            _dbContext = dbContext;
            _tenantContext = tenantContext;
            _tenantDbContextFactory = tenantDbContextFactory;
            _userContext = userContext;
            _reportExporter = reportExporter;
        }

        [HttpGet("trend")]
        public async Task<ActionResult<ReconciliationTrendResponse>> GetTrend(
            [FromQuery] DateTime? fromUtc,
            [FromQuery] DateTime? toUtc,
            [FromQuery] string? level,
            CancellationToken ct)
        {
            var auth = await AuthorizeTenantUserAsync(ct);
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            var range = ResolveRange(fromUtc, toUtc);
            if (range.Error != null) return range.Error;

            var days = await QueryDaysAsync(tenantDb, range.From, range.To, level, ct);
            return Ok(new ReconciliationTrendResponse(range.From, range.To, days));
        }

        [HttpGet("trend/export")]
        public async Task<IActionResult> ExportTrend(
            [FromQuery] string? format,
            [FromQuery] DateTime? fromUtc,
            [FromQuery] DateTime? toUtc,
            [FromQuery] string? level,
            CancellationToken ct)
        {
            var auth = await AuthorizeTenantUserAsync(ct);
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            if (!_reportExporter.TryParseFormat(format, out var exportFormat))
            {
                return BadRequest(new { message = "Unsupported export format. Use 'csv' or 'xlsx'." });
            }

            var range = ResolveRange(fromUtc, toUtc);
            if (range.Error != null) return range.Error;

            var days = await QueryDaysAsync(tenantDb, range.From, range.To, level, ct);
            if (days.Count > _reportExporter.MaxRows)
            {
                return BadRequest(new { message = $"Export limited to {_reportExporter.MaxRows} rows. Narrow the date range and try again." });
            }

            var file = _reportExporter.Export(days, TrendExportColumns, "Reconciliation Trend", exportFormat);
            return File(file.Content, file.ContentType, $"reconciliation-trend-{DateTime.UtcNow:yyyyMMddHHmmss}.{file.FileExtension}");
        }

        private static async Task<List<ReconciliationTrendDayDto>> QueryDaysAsync(
            TenantDbContext tenantDb,
            DateTime fromUtc,
            DateTime toUtc,
            string? level,
            CancellationToken ct)
        {
            var query = tenantDb.ReconciliationDailySnapshots
                .AsNoTracking()
                .Where(s => s.SnapshotDate >= fromUtc.Date && s.SnapshotDate <= toUtc.Date);

            if (!string.IsNullOrWhiteSpace(level))
            {
                query = query.Where(s => s.MatchLevel == level);
            }

            return await query
                .OrderBy(s => s.SnapshotDate)
                .ThenBy(s => s.MatchLevel)
                .Select(s => new ReconciliationTrendDayDto(
                    s.SnapshotDate,
                    s.MatchLevel,
                    s.MatchedCount,
                    s.ConfirmedCount,
                    s.ExceptionCount,
                    s.UnmatchedCount,
                    s.AverageTimeToMatchHours))
                .ToListAsync(ct);
        }

        private static (DateTime From, DateTime To, ActionResult? Error) ResolveRange(DateTime? fromUtc, DateTime? toUtc)
        {
            var to = (toUtc ?? DateTime.UtcNow).Date;
            var from = (fromUtc ?? to.AddDays(-30)).Date;

            if (from > to)
            {
                return (from, to, new BadRequestObjectResult(new { message = "fromUtc must be before or equal to toUtc." }));
            }

            if ((to - from).TotalDays > MaxTrendDays)
            {
                return (from, to, new BadRequestObjectResult(new { message = $"Date range cannot exceed {MaxTrendDays} days." }));
            }

            return (from, to, null);
        }

        private async Task<(TenantDbContext? Db, ActionResult? Error)> AuthorizeTenantUserAsync(CancellationToken ct)
        {
            if (_userContext.UserId is not { } userId) return (null, Unauthorized());

            var tenant = await _tenantContext.ResolveAsync(ct);
            if (tenant == null) return (null, Forbid());

            var isTenantMember = await _dbContext.TenantUsers.AsNoTracking()
                .AnyAsync(tu => tu.TenantId == tenant.TenantId && tu.UserId == userId, ct);
            if (!isTenantMember) return (null, Forbid());

            var tenantDb = await _tenantDbContextFactory.CreateAsync(tenant.TenantId, ct);
            var isActiveInTenant = await tenantDb.TenantUsers.AsNoTracking()
                .AnyAsync(tu => tu.UserId == userId && tu.IsActive, ct);
            if (!isActiveInTenant)
            {
                await tenantDb.DisposeAsync();
                return (null, Forbid());
            }

            return (tenantDb, null);
        }
    }
}
