using finrecon360_backend.Authorization;
using finrecon360_backend.Data;
using finrecon360_backend.Dtos.Dashboard;
using finrecon360_backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace finrecon360_backend.Controllers.Admin
{
    /// <summary>
    /// Exposes KPI reporting snapshots for the Reconciliation/Matcher workload.
    ///
    /// WHY this controller exists separately from ReconciliationController:
    /// ReconciliationController owns the operational match-group lifecycle (confirm, post-journal,
    /// waiting-queue). This controller serves the read-only, aggregated reporting layer built on
    /// top of the ReportSnapshot table populated by the ReportingSnapshotWorker background service.
    /// Keeping them separate preserves SRP and allows dashboard-specific caching or rate-limiting
    /// in the future without impacting transactional endpoints.
    /// </summary>
    [ApiController]
    [Route("api/admin/dashboard/reconciliation-kpis")]
    [Authorize]
    public class DashboardController : ControllerBase
    {
        private readonly ITenantContext _tenantContext;
        private readonly ITenantDbContextFactory _tenantDbContextFactory;

        public DashboardController(
            ITenantContext tenantContext,
            ITenantDbContextFactory tenantDbContextFactory)
        {
            _tenantContext = tenantContext;
            _tenantDbContextFactory = tenantDbContextFactory;
        }

        // ─── Latest Snapshot ─────────────────────────────────────────────────────

        /// <summary>
        /// Returns the most recent KPI snapshot for this tenant.
        /// Powers the top-level dashboard summary cards.
        /// </summary>
        [HttpGet("latest")]
        [RequirePermission("ADMIN.RECONCILIATION.VIEW")]
        public async Task<ActionResult<ReportSnapshotDto>> GetLatestSnapshot(
            CancellationToken cancellationToken = default)
        {
            var tenant = await _tenantContext.ResolveAsync(cancellationToken);
            if (tenant is null) return Unauthorized();

            await using var tenantDb = await _tenantDbContextFactory.CreateAsync(tenant.TenantId, cancellationToken);

            var latest = await tenantDb.ReportSnapshots
                .AsNoTracking()
                .OrderByDescending(s => s.SnapshotDate)
                .FirstOrDefaultAsync(cancellationToken);

            if (latest is null)
                return NotFound(new { message = "No KPI snapshots have been generated yet." });

            return Ok(MapToDto(latest));
        }

        // ─── Historical Snapshots ────────────────────────────────────────────────

        /// <summary>
        /// Returns KPI snapshots within the specified date range, ordered ascending.
        /// Powers the historical trend charts on the dashboard.
        /// </summary>
        [HttpGet("history")]
        [RequirePermission("ADMIN.RECONCILIATION.VIEW")]
        public async Task<ActionResult<List<ReportSnapshotDto>>> GetSnapshotHistory(
            [FromQuery] DateTime startDate,
            [FromQuery] DateTime endDate,
            CancellationToken cancellationToken = default)
        {
            if (startDate > endDate)
                return BadRequest(new { message = "startDate must be before or equal to endDate." });

            var tenant = await _tenantContext.ResolveAsync(cancellationToken);
            if (tenant is null) return Unauthorized();

            await using var tenantDb = await _tenantDbContextFactory.CreateAsync(tenant.TenantId, cancellationToken);

            var snapshots = await tenantDb.ReportSnapshots
                .AsNoTracking()
                .Where(s => s.SnapshotDate >= startDate.Date && s.SnapshotDate <= endDate.Date)
                .OrderBy(s => s.SnapshotDate)
                .ToListAsync(cancellationToken);

            return Ok(snapshots.Select(MapToDto).ToList());
        }

        // ─── Private Mapping ─────────────────────────────────────────────────────

        private static ReportSnapshotDto MapToDto(Models.ReportSnapshot entity)
        {
            return new ReportSnapshotDto
            {
                ReportSnapshotId = entity.ReportSnapshotId,
                SnapshotDate = entity.SnapshotDate,
                TotalUnmatchedCardCashouts = entity.TotalUnmatchedCardCashouts,
                PendingExceptions = entity.PendingExceptions,
                TotalJournalReady = entity.TotalJournalReady,
                ReconciliationCompletionPercentage = entity.ReconciliationCompletionPercentage,
                TotalMatchGroupsConfirmed = entity.TotalMatchGroupsConfirmed,
                TotalFeeAdjustments = entity.TotalFeeAdjustments,
                CreatedAt = entity.CreatedAt
            };
        }
    }
}
