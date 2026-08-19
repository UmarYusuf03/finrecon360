using finrecon360_backend.Data;
using finrecon360_backend.Models;
using finrecon360_backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace finrecon360_backend.Controllers.Admin
{
    // ── Response DTO ──────────────────────────────────────────────────────────────────

    public record DashboardSummaryResponse(
        int TotalTransactions,
        int PendingApprovalTransactions,
        int NeedsBankMatchTransactions,
        int JournalReadyTransactions,
        int TotalMatchGroups,
        int ConfirmedMatchGroups,
        int PendingConfirmationMatchGroups,
        int TotalEvents,
        int ExceptionEvents,
        int TotalJournalEntries,
        int TotalBankAccounts,
        DateTime LastUpdatedUtc);

    public record DashboardTrendDayDto(
        DateTime SnapshotDate,
        int PendingApprovalCount,
        decimal? OldestPendingApprovalAgeHours,
        int JournalEntriesPostedCount,
        decimal JournalDebitAmountPosted,
        int BankRecordsTotalCount,
        int BankRecordsMatchedCount);

    public record DashboardTrendResponse(
        DateTime FromUtc,
        DateTime ToUtc,
        IReadOnlyList<DashboardTrendDayDto> Days);

    // ── Controller ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Tenant-scoped dashboard counts. Replaces the previous hardcoded /api/dashboard/summary
    /// mock: every field here is a real COUNT against Transactions, ReconciliationMatchGroups,
    /// ReconciliationEvents and JournalEntries for the caller's tenant.
    /// </summary>
    [ApiController]
    [Route("api/admin/dashboard")]
    [Authorize]
    public class DashboardController : ControllerBase
    {
        private readonly AppDbContext _dbContext;
        private readonly ITenantContext _tenantContext;
        private readonly ITenantDbContextFactory _tenantDbContextFactory;
        private readonly IUserContext _userContext;

        public DashboardController(
            AppDbContext dbContext,
            ITenantContext tenantContext,
            ITenantDbContextFactory tenantDbContextFactory,
            IUserContext userContext)
        {
            _dbContext = dbContext;
            _tenantContext = tenantContext;
            _tenantDbContextFactory = tenantDbContextFactory;
            _userContext = userContext;
        }

        [HttpGet("summary")]
        public async Task<ActionResult<DashboardSummaryResponse>> GetSummary(
            [FromQuery] DateTime? startDate,
            [FromQuery] DateTime? endDate,
            CancellationToken ct)
        {
            if (_userContext.UserId is not { } userId) return Unauthorized();

            var tenant = await _tenantContext.ResolveAsync(ct);
            if (tenant == null) return Forbid();

            var isTenantMember = await _dbContext.TenantUsers.AsNoTracking()
                .AnyAsync(tu => tu.TenantId == tenant.TenantId && tu.UserId == userId, ct);
            if (!isTenantMember) return Forbid();

            await using var tenantDb = await _tenantDbContextFactory.CreateAsync(tenant.TenantId, ct);

            var isActiveInTenant = await tenantDb.TenantUsers.AsNoTracking()
                .AnyAsync(tu => tu.UserId == userId && tu.IsActive, ct);
            if (!isActiveInTenant) return Forbid();

            var txQuery = tenantDb.Transactions.AsNoTracking();
            var mgQuery = tenantDb.ReconciliationMatchGroups.AsNoTracking();
            var reQuery = tenantDb.ReconciliationEvents.AsNoTracking();
            var jeQuery = tenantDb.JournalEntries.AsNoTracking();

            if (startDate.HasValue)
            {
                txQuery = txQuery.Where(t => t.CreatedAt >= startDate.Value);
                mgQuery = mgQuery.Where(g => g.CreatedAt >= startDate.Value);
                reQuery = reQuery.Where(e => e.CreatedAt >= startDate.Value);
                jeQuery = jeQuery.Where(j => j.PostedAt >= startDate.Value);
            }

            if (endDate.HasValue)
            {
                txQuery = txQuery.Where(t => t.CreatedAt <= endDate.Value);
                mgQuery = mgQuery.Where(g => g.CreatedAt <= endDate.Value);
                reQuery = reQuery.Where(e => e.CreatedAt <= endDate.Value);
                jeQuery = jeQuery.Where(j => j.PostedAt <= endDate.Value);
            }

            var totalTransactions = await txQuery.CountAsync(ct);
            var pendingApprovalTransactions = await txQuery
                .CountAsync(t => t.TransactionState == TransactionState.Pending, ct);
            var needsBankMatchTransactions = await txQuery
                .CountAsync(t => t.TransactionState == TransactionState.NeedsBankMatch, ct);
            var journalReadyTransactions = await txQuery
                .CountAsync(t => t.TransactionState == TransactionState.JournalReady, ct);

            var totalMatchGroups = await mgQuery.CountAsync(ct);
            var confirmedMatchGroups = await mgQuery
                .CountAsync(g => g.IsConfirmed, ct);
            var pendingConfirmationMatchGroups = totalMatchGroups - confirmedMatchGroups;

            var totalEvents = await reQuery.CountAsync(ct);
            var exceptionEvents = await reQuery
                .CountAsync(e => e.EventType == "Variance" || e.EventType == "RequiresReview", ct);

            var totalJournalEntries = await jeQuery.CountAsync(ct);
            var totalBankAccounts = await tenantDb.BankAccounts.AsNoTracking().CountAsync(ct);

            return Ok(new DashboardSummaryResponse(
                totalTransactions,
                pendingApprovalTransactions,
                needsBankMatchTransactions,
                journalReadyTransactions,
                totalMatchGroups,
                confirmedMatchGroups,
                pendingConfirmationMatchGroups,
                totalEvents,
                exceptionEvents,
                totalJournalEntries,
                totalBankAccounts,
                DateTime.UtcNow));
        }

        /// <summary>
        /// Period-based trend KPIs (Section 17's term) read from TenantDailySnapshot — populated
        /// once daily by ReconciliationSnapshotHostedService, so today (and any day before the
        /// first snapshot ran) will have no row yet. Deliberately separate from GetSummary above:
        /// that endpoint's counts are live, current-moment queue sizes an operator needs to be
        /// accurate to the second (pending approvals, needs-bank-match, journal-ready) — swapping
        /// those to a once-daily snapshot would show yesterday's queue as if it were today's,
        /// which is actively misleading, not just "slightly stale". This endpoint is additive,
        /// for historical trend context alongside the live summary, not a replacement for it.
        /// </summary>
        [HttpGet("trend")]
        public async Task<ActionResult<DashboardTrendResponse>> GetTrend(
            [FromQuery] DateTime? fromUtc,
            [FromQuery] DateTime? toUtc,
            CancellationToken ct)
        {
            if (_userContext.UserId is not { } userId) return Unauthorized();

            var tenant = await _tenantContext.ResolveAsync(ct);
            if (tenant == null) return Forbid();

            var isTenantMember = await _dbContext.TenantUsers.AsNoTracking()
                .AnyAsync(tu => tu.TenantId == tenant.TenantId && tu.UserId == userId, ct);
            if (!isTenantMember) return Forbid();

            await using var tenantDb = await _tenantDbContextFactory.CreateAsync(tenant.TenantId, ct);

            var isActiveInTenant = await tenantDb.TenantUsers.AsNoTracking()
                .AnyAsync(tu => tu.UserId == userId && tu.IsActive, ct);
            if (!isActiveInTenant) return Forbid();

            var to = (toUtc ?? DateTime.UtcNow).Date;
            var from = (fromUtc ?? to.AddDays(-30)).Date;
            if (from > to)
            {
                return BadRequest(new { message = "fromUtc must be before or equal to toUtc." });
            }

            var days = await tenantDb.TenantDailySnapshots
                .AsNoTracking()
                .Where(s => s.SnapshotDate >= from && s.SnapshotDate <= to)
                .OrderBy(s => s.SnapshotDate)
                .Select(s => new DashboardTrendDayDto(
                    s.SnapshotDate,
                    s.PendingApprovalCount,
                    s.OldestPendingApprovalAgeHours,
                    s.JournalEntriesPostedCount,
                    s.JournalDebitAmountPosted,
                    s.BankRecordsTotalCount,
                    s.BankRecordsMatchedCount))
                .ToListAsync(ct);

            return Ok(new DashboardTrendResponse(from, to, days));
        }
    }
}
