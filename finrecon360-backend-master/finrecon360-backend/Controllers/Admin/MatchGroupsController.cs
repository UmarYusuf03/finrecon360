using finrecon360_backend.Authorization;
using finrecon360_backend.Data;
using finrecon360_backend.Services;
using finrecon360_backend.Services.Export;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace finrecon360_backend.Controllers.Admin
{
    // ── Request DTOs ──────────────────────────────────────────────────────────────────

    public record ConfirmMatchRequest(string? Note);
    public record RejectMatchRequest(string Reason);

    // ── Controller ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Exposes reconciliation match-group management endpoints:
    ///
    ///   GET  /api/admin/match-groups/pending?level=Level4
    ///   POST /api/admin/match-groups/{id}/confirm
    ///   POST /api/admin/match-groups/{id}/reject
    ///   GET  /api/admin/match-groups/unmatched?rule=Expense&amp;from=&amp;to=
    ///
    /// All endpoints are tenant-scoped: they operate on the caller's tenant database.
    /// Requires the ADMIN.RECONCILIATION.CONFIRM permission.
    /// </summary>
    [ApiController]
    [Route("api/admin/match-groups")]
    [Authorize]
    public class MatchGroupsController : ControllerBase
    {
        private static readonly IReadOnlyList<ExportColumn<PendingMatchSummary>> PendingExportColumns = new List<ExportColumn<PendingMatchSummary>>
        {
            new("Match Level", g => g.MatchLevel),
            new("Settlement Key", g => g.SettlementKey),
            new("Matched Amount", g => g.MatchedAmount.ToString("0.00")),
            new("Variance", g => g.Variance.ToString("0.00")),
            new("Status", g => g.Status),
            new("Created At (UTC)", g => g.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")),
            new("Record Count", g => g.Records.Count.ToString()),
        };

        private static readonly IReadOnlyList<ExportColumn<UnmatchedQueueItem>> UnmatchedExportColumns = new List<ExportColumn<UnmatchedQueueItem>>
        {
            new("Date", i => i.TransactionDate.ToString("yyyy-MM-dd")),
            new("Source", i => i.SourceType),
            new("Match Rule", i => i.MatchRule),
            new("Reference", i => i.ReferenceNumber),
            new("Amount", i => i.Amount.ToString("0.00")),
            new("Reason", i => i.UnmatchedReason),
            new("Hint", i => i.Hint?.HintMessage),
        };

        private readonly AppDbContext _dbContext;
        private readonly ITenantContext _tenantContext;
        private readonly ITenantDbContextFactory _tenantDbContextFactory;
        private readonly IUserContext _userContext;
        private readonly IReconciliationMatchConfirmationService _matchService;
        private readonly IReportExporter _reportExporter;

        public MatchGroupsController(
            AppDbContext dbContext,
            ITenantContext tenantContext,
            ITenantDbContextFactory tenantDbContextFactory,
            IUserContext userContext,
            IReconciliationMatchConfirmationService matchService,
            IReportExporter reportExporter)
        {
            _dbContext = dbContext;
            _tenantContext = tenantContext;
            _tenantDbContextFactory = tenantDbContextFactory;
            _userContext = userContext;
            _matchService = matchService;
            _reportExporter = reportExporter;
        }

        /// <summary>
        /// Returns reconciliation match groups that are awaiting human confirmation.
        /// Optionally filtered to a specific MatchLevel (e.g. Level4, Level6).
        /// </summary>
        [HttpGet("pending")]
        [RequirePermission("ADMIN.RECONCILIATION.CONFIRM")]
        public async Task<ActionResult<List<PendingMatchSummary>>> GetPendingMatches(
            [FromQuery] string? level,
            CancellationToken ct)
        {
            var auth = await AuthorizeTenantUserAsync(ct);
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            var result = await _matchService.GetPendingMatchesAsync(tenantDb, level, ct);
            return Ok(result);
        }

        /// <summary>
        /// Returns high-level summary counts of pending confirmations, exceptions, and unmatched items
        /// across all reconciliation rules. Used for the Matcher Dashboard.
        /// </summary>
        [HttpGet("summary")]
        [RequirePermission("ADMIN.RECONCILIATION.CONFIRM")]
        public async Task<ActionResult<MatcherSummary>> GetSummary(CancellationToken ct)
        {
            var auth = await AuthorizeTenantUserAsync(ct);
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            var result = await _matchService.GetSummaryAsync(tenantDb, ct);
            return Ok(result);
        }

        /// <summary>
        /// Exports the pending confirmation queue (same data as GET /pending) as CSV or XLSX.
        /// </summary>
        [HttpGet("pending/export")]
        [RequirePermission("ADMIN.RECONCILIATION.CONFIRM")]
        public async Task<IActionResult> ExportPendingMatches(
            [FromQuery] string? format,
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

            var result = await _matchService.GetPendingMatchesAsync(tenantDb, level, ct);
            if (result.Count > _reportExporter.MaxRows)
            {
                return BadRequest(new { message = $"Export limited to {_reportExporter.MaxRows} rows. Narrow the level filter and try again." });
            }

            var file = _reportExporter.Export(result, PendingExportColumns, "Pending Matches", exportFormat);
            return File(file.Content, file.ContentType, $"pending-matches-{DateTime.UtcNow:yyyyMMddHHmmss}.{file.FileExtension}");
        }

        /// <summary>
        /// Exports the unmatched items queue (same data as GET /unmatched) as CSV or XLSX.
        /// </summary>
        [HttpGet("unmatched/export")]
        [RequirePermission("ADMIN.RECONCILIATION.CONFIRM")]
        public async Task<IActionResult> ExportUnmatched(
            [FromQuery] string? format,
            [FromQuery] string? rule,
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            CancellationToken ct)
        {
            var auth = await AuthorizeTenantUserAsync(ct);
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            if (!_reportExporter.TryParseFormat(format, out var exportFormat))
            {
                return BadRequest(new { message = "Unsupported export format. Use 'csv' or 'xlsx'." });
            }

            var result = await _matchService.GetUnmatchedQueueAsync(tenantDb, rule, from, to, ct);
            if (result.Count > _reportExporter.MaxRows)
            {
                return BadRequest(new { message = $"Export limited to {_reportExporter.MaxRows} rows. Narrow the rule or date range and try again." });
            }

            var file = _reportExporter.Export(result, UnmatchedExportColumns, "Unmatched Items", exportFormat);
            return File(file.Content, file.ContentType, $"unmatched-items-{DateTime.UtcNow:yyyyMMddHHmmss}.{file.FileExtension}");
        }

        /// <summary>
        /// Confirms a pending match group. The linked card-cashout transaction (if any)
        /// is promoted from NeedsBankMatch → JournalReady, unlocking journal posting.
        /// </summary>
        [HttpPost("{id:guid}/confirm")]
        [RequirePermission("ADMIN.RECONCILIATION.CONFIRM")]
        public async Task<IActionResult> ConfirmMatch(
            Guid id,
            [FromBody] ConfirmMatchRequest request,
            CancellationToken ct)
        {
            var auth = await AuthorizeTenantUserAsync(ct);
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            try
            {
                var success = await _matchService.ConfirmMatchAsync(
                    tenantDb, id, request.Note, auth.UserId!.Value, ct);

                if (!success)
                {
                    return NotFound(new { message = "Match group not found or already confirmed." });
                }

                return Ok(new { message = "Match confirmed. Transaction promoted to JournalReady." });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Rejects a pending match group. The imported records are reset to PENDING
        /// so the matcher can attempt re-matching on the next run.
        /// </summary>
        [HttpPost("{id:guid}/reject")]
        [RequirePermission("ADMIN.RECONCILIATION.CONFIRM")]
        public async Task<IActionResult> RejectMatch(
            Guid id,
            [FromBody] RejectMatchRequest request,
            CancellationToken ct)
        {
            var auth = await AuthorizeTenantUserAsync(ct);
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            try
            {
                var success = await _matchService.RejectMatchAsync(
                    tenantDb, id, request.Reason, auth.UserId!.Value, ct);

                if (!success)
                {
                    return NotFound(new { message = "Match group not found." });
                }

                return Ok(new { message = "Match rejected. Records returned to pending state." });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Returns unmatched imported records with contextual hints.
        ///
        /// Key feature: if a bank statement line has no approved expense to match against
        /// but there IS a Pending (unapproved) card cashout for the same amount and date,
        /// the response includes a hint message explaining what the user needs to do.
        ///
        /// HintType values:
        ///   MatchingItemPendingApproval  — unapproved expense exists; approve to unlock matching
        ///   MatchingItemNeedsBankMatch   — approved expense is already in the matcher queue
        ///   NoPossibleMatch              — no corresponding expense record found at all
        /// </summary>
        [HttpGet("unmatched")]
        [RequirePermission("ADMIN.RECONCILIATION.CONFIRM")]
        public async Task<ActionResult<List<UnmatchedQueueItem>>> GetUnmatched(
            [FromQuery] string? rule,
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            CancellationToken ct)
        {
            var auth = await AuthorizeTenantUserAsync(ct);
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            var result = await _matchService.GetUnmatchedQueueAsync(tenantDb, rule, from, to, ct);
            return Ok(result);
        }

        // ── Auth helper (same pattern as TransactionsController) ──────────────────────

        private async Task<(TenantDbContext? Db, Guid? UserId, ActionResult? Error)> AuthorizeTenantUserAsync(
            CancellationToken ct)
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
