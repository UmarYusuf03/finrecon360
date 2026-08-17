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
        public async Task<ActionResult<DashboardSummaryResponse>> GetSummary(CancellationToken ct)
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

            var totalTransactions = await tenantDb.Transactions.AsNoTracking().CountAsync(ct);
            var pendingApprovalTransactions = await tenantDb.Transactions.AsNoTracking()
                .CountAsync(t => t.TransactionState == TransactionState.Pending, ct);
            var needsBankMatchTransactions = await tenantDb.Transactions.AsNoTracking()
                .CountAsync(t => t.TransactionState == TransactionState.NeedsBankMatch, ct);
            var journalReadyTransactions = await tenantDb.Transactions.AsNoTracking()
                .CountAsync(t => t.TransactionState == TransactionState.JournalReady, ct);

            var totalMatchGroups = await tenantDb.ReconciliationMatchGroups.AsNoTracking().CountAsync(ct);
            var confirmedMatchGroups = await tenantDb.ReconciliationMatchGroups.AsNoTracking()
                .CountAsync(g => g.IsConfirmed, ct);
            var pendingConfirmationMatchGroups = totalMatchGroups - confirmedMatchGroups;

            var totalEvents = await tenantDb.ReconciliationEvents.AsNoTracking().CountAsync(ct);
            var exceptionEvents = await tenantDb.ReconciliationEvents.AsNoTracking()
                .CountAsync(e => e.EventType == "Variance" || e.EventType == "RequiresReview", ct);

            var totalJournalEntries = await tenantDb.JournalEntries.AsNoTracking().CountAsync(ct);
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
    }
}
