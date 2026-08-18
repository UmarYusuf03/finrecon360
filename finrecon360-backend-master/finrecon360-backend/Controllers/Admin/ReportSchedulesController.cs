using finrecon360_backend.Authorization;
using finrecon360_backend.Data;
using finrecon360_backend.Dtos.Reporting;
using finrecon360_backend.Services;
using finrecon360_backend.Services.Reporting;
using finrecon360_backend.Models;
using finrecon360_backend.BackgroundServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace finrecon360_backend.Controllers.Admin
{
    /// <summary>
    /// CRUD for "email me this report every &lt;weekday&gt;" schedules. Actual delivery happens in
    /// ReportScheduleHostedService; this controller only manages the ReportSchedule rows it reads.
    /// </summary>
    [ApiController]
    [Route("api/admin/report-schedules")]
    [Authorize]
    [RequirePermission("ADMIN.REPORT_SCHEDULES.MANAGE")]
    public class ReportSchedulesController : ControllerBase
    {
        private static readonly string[] ValidFormats = { "csv", "xlsx" };

        private readonly AppDbContext _dbContext;
        private readonly ITenantContext _tenantContext;
        private readonly ITenantDbContextFactory _tenantDbContextFactory;
        private readonly IUserContext _userContext;
        private readonly IScheduledReportRenderer _reportRenderer;

        public ReportSchedulesController(
            AppDbContext dbContext,
            ITenantContext tenantContext,
            ITenantDbContextFactory tenantDbContextFactory,
            IUserContext userContext,
            IScheduledReportRenderer reportRenderer)
        {
            _dbContext = dbContext;
            _tenantContext = tenantContext;
            _tenantDbContextFactory = tenantDbContextFactory;
            _userContext = userContext;
            _reportRenderer = reportRenderer;
        }

        [HttpGet]
        public async Task<ActionResult<List<ReportScheduleResponse>>> GetAll(CancellationToken ct)
        {
            var auth = await AuthorizeTenantUserAsync(ct);
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            var schedules = await tenantDb.ReportSchedules
                .AsNoTracking()
                .OrderBy(s => s.ReportType)
                .Select(s => ToResponse(s))
                .ToListAsync(ct);

            return Ok(schedules);
        }

        [HttpPost]
        public async Task<ActionResult<ReportScheduleResponse>> Create(
            [FromBody] CreateReportScheduleRequest request,
            CancellationToken ct)
        {
            var auth = await AuthorizeTenantUserAsync(ct);
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            if (!_reportRenderer.IsKnownReportType(request.ReportType))
            {
                return BadRequest(new { message = $"Unknown report type '{request.ReportType}'." });
            }

            var format = request.Format?.Trim().ToLowerInvariant() ?? "csv";
            if (!ValidFormats.Contains(format))
            {
                return BadRequest(new { message = "Format must be 'csv' or 'xlsx'." });
            }

            var now = DateTime.UtcNow;
            var entity = new ReportSchedule
            {
                ReportScheduleId = Guid.NewGuid(),
                ReportType = request.ReportType,
                Format = format,
                DayOfWeek = request.DayOfWeek,
                RecipientEmail = request.RecipientEmail.Trim(),
                IsActive = true,
                CreatedByUserId = auth.UserId!.Value,
                CreatedAt = now,
                NextRunAt = ReportScheduleHostedService.ComputeNextRunAt(now, (DayOfWeek)request.DayOfWeek),
            };

            tenantDb.ReportSchedules.Add(entity);
            await tenantDb.SaveChangesAsync(ct);

            return CreatedAtAction(nameof(GetAll), ToResponse(entity));
        }

        [HttpPut("{id:guid}/active")]
        public async Task<IActionResult> SetActive(
            Guid id,
            [FromBody] UpdateReportScheduleActiveRequest request,
            CancellationToken ct)
        {
            var auth = await AuthorizeTenantUserAsync(ct);
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            var entity = await tenantDb.ReportSchedules.FirstOrDefaultAsync(s => s.ReportScheduleId == id, ct);
            if (entity == null) return NotFound();

            entity.IsActive = request.IsActive;
            // Reactivating a long-paused schedule shouldn't immediately fire for every week it
            // missed — push it out to the next real occurrence instead.
            if (request.IsActive)
            {
                entity.NextRunAt = ReportScheduleHostedService.ComputeNextRunAt(DateTime.UtcNow, (DayOfWeek)entity.DayOfWeek);
            }

            await tenantDb.SaveChangesAsync(ct);
            return NoContent();
        }

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        {
            var auth = await AuthorizeTenantUserAsync(ct);
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            var entity = await tenantDb.ReportSchedules.FirstOrDefaultAsync(s => s.ReportScheduleId == id, ct);
            if (entity == null) return NotFound();

            tenantDb.ReportSchedules.Remove(entity);
            await tenantDb.SaveChangesAsync(ct);
            return NoContent();
        }

        private static ReportScheduleResponse ToResponse(ReportSchedule s) => new(
            s.ReportScheduleId, s.ReportType, s.Format, s.DayOfWeek, s.RecipientEmail,
            s.IsActive, s.LastRunAt, s.NextRunAt, s.CreatedAt);

        private async Task<(TenantDbContext? Db, Guid? UserId, ActionResult? Error)> AuthorizeTenantUserAsync(CancellationToken ct)
        {
            if (_userContext.UserId is not { } userId) return (null, null, Unauthorized());

            var tenant = await _tenantContext.ResolveAsync(ct);
            if (tenant == null) return (null, null, Forbid());

            var isTenantMember = await _dbContext.TenantUsers.AsNoTracking()
                .AnyAsync(tu => tu.TenantId == tenant.TenantId && tu.UserId == userId, ct);
            if (!isTenantMember) return (null, null, Forbid());

            var tenantDb = await _tenantDbContextFactory.CreateAsync(tenant.TenantId, ct);
            var isActiveInTenant = await tenantDb.TenantUsers.AsNoTracking()
                .AnyAsync(tu => tu.UserId == userId && tu.IsActive, ct);
            if (!isActiveInTenant)
            {
                await tenantDb.DisposeAsync();
                return (null, null, Forbid());
            }

            return (tenantDb, userId, null);
        }
    }
}
