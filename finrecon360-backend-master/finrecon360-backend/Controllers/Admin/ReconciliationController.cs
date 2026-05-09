using finrecon360_backend.Authorization;
using finrecon360_backend.Data;
using finrecon360_backend.Dtos.Reconciliation;
using finrecon360_backend.Models;
using finrecon360_backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace finrecon360_backend.Controllers.Admin
{
    /// <summary>
    /// Exposes the reconciliation domain to the frontend.
    /// Covers match groups, reconciliation events, the waiting queue (missing SettlementID),
    /// human confirmation of match groups, and journal posting.
    ///
    /// WHY this controller exists separately from TransactionsController:
    /// The reconciliation domain owns match groups and events; the transaction domain owns
    /// the approve/reject lifecycle. Keeping them separate preserves SRP and allows
    /// the MATCHER.VIEW and ADMIN.RECONCILIATION.VIEW permissions to be scoped independently.
    /// </summary>
    [ApiController]
    [Route("api/admin/reconciliation")]
    [Authorize]
    public class ReconciliationController : ControllerBase
    {
        private readonly ITenantContext _tenantContext;
        private readonly ITenantDbContextFactory _tenantDbContextFactory;
        private readonly IUserContext _userContext;

        public ReconciliationController(
            ITenantContext tenantContext,
            ITenantDbContextFactory tenantDbContextFactory,
            IUserContext userContext)
        {
            _tenantContext = tenantContext;
            _tenantDbContextFactory = tenantDbContextFactory;
            _userContext = userContext;
        }

        // ─── Match Groups ────────────────────────────────────────────────────────

        /// <summary>
        /// Returns reconciliation match groups for this tenant.
        /// Filter by matchLevel (Level3 | Level4) and/or isConfirmed to narrow results.
        /// </summary>
        [HttpGet("match-groups")]
        [RequirePermission("ADMIN.RECONCILIATION.VIEW")]
        public async Task<ActionResult<List<ReconciliationMatchGroupResponse>>> GetMatchGroups(
            [FromQuery] string? matchLevel = null,
            [FromQuery] bool? isConfirmed = null,
            [FromQuery] bool? isJournalPosted = null,
            CancellationToken cancellationToken = default)
        {
            var tenant = await _tenantContext.ResolveAsync(cancellationToken);
            if (tenant is null) return Unauthorized();

            await using var tenantDb = await _tenantDbContextFactory.CreateAsync(tenant.TenantId, cancellationToken);

            var query = tenantDb.ReconciliationMatchGroups
                .Include(g => g.MatchedRecords)
                    .ThenInclude(mr => mr.ImportedNormalizedRecord)
                .AsNoTracking()
                .AsQueryable();

            if (!string.IsNullOrEmpty(matchLevel))
                query = query.Where(g => g.MatchLevel == matchLevel);

            if (isConfirmed.HasValue)
                query = query.Where(g => g.IsConfirmed == isConfirmed.Value);

            if (isJournalPosted.HasValue)
                query = query.Where(g => g.IsJournalPosted == isJournalPosted.Value);

            var groups = await query
                .OrderByDescending(g => g.CreatedAt)
                .ToListAsync(cancellationToken);

            return Ok(groups.Select(MapGroupToResponse).ToList());
        }

        /// <summary>
        /// Returns a single match group by ID, with all matched record details.
        /// </summary>
        [HttpGet("match-groups/{id:guid}")]
        [RequirePermission("ADMIN.RECONCILIATION.VIEW")]
        public async Task<ActionResult<ReconciliationMatchGroupResponse>> GetMatchGroup(
            Guid id,
            CancellationToken cancellationToken = default)
        {
            var tenant = await _tenantContext.ResolveAsync(cancellationToken);
            if (tenant is null) return Unauthorized();

            await using var tenantDb = await _tenantDbContextFactory.CreateAsync(tenant.TenantId, cancellationToken);

            var group = await tenantDb.ReconciliationMatchGroups
                .Include(g => g.MatchedRecords)
                    .ThenInclude(mr => mr.ImportedNormalizedRecord)
                .AsNoTracking()
                .FirstOrDefaultAsync(g => g.ReconciliationMatchGroupId == id, cancellationToken);

            if (group is null)
                return NotFound();

            return Ok(MapGroupToResponse(group));
        }

        /// <summary>
        /// Human-confirmation gate: marks a match group as confirmed by the current user.
        /// Journal posting is unlocked only after this call succeeds.
        /// Idempotent — confirming an already-confirmed group is a no-op with 200 OK.
        /// </summary>
        [HttpPost("match-groups/{id:guid}/confirm")]
        // WHY: CONFIRM is its own permission so a MANAGER can confirm without owning full MANAGE.
        // Existing ADMIN.RECONCILIATION.MANAGE grants are still accepted via the AliasMap implication.
        [RequirePermission("ADMIN.RECONCILIATION.CONFIRM")]
        public async Task<ActionResult<ReconciliationMatchGroupResponse>> ConfirmMatchGroup(
            Guid id,
            CancellationToken cancellationToken = default)
        {
            var tenant = await _tenantContext.ResolveAsync(cancellationToken);
            if (tenant is null) return Unauthorized();

            await using var tenantDb = await _tenantDbContextFactory.CreateAsync(tenant.TenantId, cancellationToken);

            var group = await tenantDb.ReconciliationMatchGroups
                .Include(g => g.MatchedRecords)
                    .ThenInclude(mr => mr.ImportedNormalizedRecord)
                .FirstOrDefaultAsync(g => g.ReconciliationMatchGroupId == id, cancellationToken);

            if (group is null)
                return NotFound();

            // Idempotent — already confirmed groups return immediately without re-writing audit data.
            if (!group.IsConfirmed)
            {
                group.IsConfirmed = true;
                group.ConfirmedByUserId = _userContext.UserId;
                group.ConfirmedAt = DateTime.UtcNow;
                group.UpdatedAt = DateTime.UtcNow;

                // Mark all normalized records in this group as MATCHED.
                foreach (var member in group.MatchedRecords)
                {
                    if (member.ImportedNormalizedRecord is not null)
                    {
                        member.ImportedNormalizedRecord.MatchStatus = "MATCHED";
                    }
                }

                await tenantDb.SaveChangesAsync(cancellationToken);
            }

            return Ok(MapGroupToResponse(group));
        }

        // ─── Reconciliation Events ───────────────────────────────────────────────

        /// <summary>
        /// Returns all reconciliation events for a given import batch.
        /// Use this to show the "what happened" panel after an import commit.
        /// Filter by stage (Level3 | Level4), sourceType (ERP | GATEWAY | BANK | POS),
        /// and/or status (Pending | Resolved | Exception).
        /// </summary>
                [HttpGet("events")]
        // WHY: CASHIER with ADMIN.RECONCILIATION.POS.RESOLVE can VIEW events but only
        // for POS batches — the AllowedSourceTypes helper enforces this automatically.
        [RequirePermission("ADMIN.RECONCILIATION.VIEW")]
        public async Task<ActionResult<List<ReconciliationEventResponse>>> GetEvents(
            [FromQuery] Guid? importBatchId = null,
            [FromQuery] string? stage = null,
            [FromQuery] string? sourceType = null,
            [FromQuery] string? status = null,
            CancellationToken cancellationToken = default)
        {
            var tenant = await _tenantContext.ResolveAsync(cancellationToken);
            if (tenant is null) return Unauthorized();

            await using var tenantDb = await _tenantDbContextFactory.CreateAsync(tenant.TenantId, cancellationToken);

            var query = tenantDb.ReconciliationEvents
                .AsNoTracking()
                .AsQueryable();

            // WHY: Source-type scope — restrict the event list to the caller's permitted types.
            // AllowedSourceTypes returns null when the user has a full (unscoped) RESOLVE/CONFIRM
            // permission, meaning no additional filtering is applied.
            var userPerms = await GetUserPermissionsAsync(tenantDb);
            var allowedTypes = SourceTypeScope.AllowedSourceTypes(userPerms, "RECONCILIATION", "RESOLVE")
                           ?? SourceTypeScope.AllowedSourceTypes(userPerms, "RECONCILIATION", "CONFIRM");
            if (allowedTypes != null && allowedTypes.Count > 0)
            {
                var typeList = allowedTypes.ToList();
                query = query.Where(e => typeList.Contains(e.SourceType));
            }

            if (importBatchId.HasValue)
                query = query.Where(e => e.ImportBatchId == importBatchId.Value);

            if (!string.IsNullOrEmpty(stage))
                query = query.Where(e => e.Stage == stage);

            // WHY: Even if the caller specifies a sourceType filter, restrict to their scope.
            // A CASHIER who passes sourceType=ERP in the query string still only sees POS.
            if (!string.IsNullOrEmpty(sourceType))
            {
                var requestedType = sourceType.Trim().ToUpperInvariant();
                if (allowedTypes == null || allowedTypes.Contains(requestedType))
                    query = query.Where(e => e.SourceType == requestedType);
                // else: their scoped permissions don't include the requested type — leave the scope filter active
            }

            if (!string.IsNullOrEmpty(status))
                query = query.Where(e => e.Status == status);

            var events = await query
                .OrderBy(e => e.CreatedAt)
                .ToListAsync(cancellationToken);

            return Ok(events.Select(e => new ReconciliationEventResponse
            {
                ReconciliationEventId = e.ReconciliationEventId,
                ImportBatchId = e.ImportBatchId,
                ImportedNormalizedRecordId = e.ImportedNormalizedRecordId,
                EventType = e.EventType,
                Stage = e.Stage,
                SourceType = e.SourceType,
                Status = e.Status,
                DetailJson = e.DetailJson,
                CreatedAt = e.CreatedAt,
                ResolvedAt = e.ResolvedAt,
            }).ToList());
        }

        // ─── Waiting Queue ────────────────────────────────────────────────────────

        /// <summary>
        /// Returns Gateway-sourced normalized records that are missing a SettlementId.
        /// These records are stuck at Level-4 matching and need staff to attach the ID manually.
        /// </summary>
        [HttpGet("waiting-queue")]
        [RequirePermission("ADMIN.RECONCILIATION.VIEW")]
        public async Task<ActionResult<List<WaitingRecordResponse>>> GetWaitingQueue(
            CancellationToken cancellationToken = default)
        {
            var tenant = await _tenantContext.ResolveAsync(cancellationToken);
            if (tenant is null) return Unauthorized();

            await using var tenantDb = await _tenantDbContextFactory.CreateAsync(tenant.TenantId, cancellationToken);

            // WHY join to ImportBatches: SourceType = 'GATEWAY' lives on the batch, not on the
            // normalized record. This keeps the normalized record table lighter.
            var records = await tenantDb.ImportedNormalizedRecords
                .Include(r => r.ImportBatch)
                .AsNoTracking()
                .Where(r => r.MatchStatus == "WAITING"
                         && r.ImportBatch != null
                         && r.ImportBatch.SourceType == "GATEWAY")
                .OrderBy(r => r.TransactionDate)
                .ToListAsync(cancellationToken);

            return Ok(records.Select(r => new WaitingRecordResponse
            {
                ImportedNormalizedRecordId = r.ImportedNormalizedRecordId,
                ImportBatchId = r.ImportBatchId,
                TransactionDate = r.TransactionDate,
                ReferenceNumber = r.ReferenceNumber,
                Description = r.Description,
                GrossAmount = r.GrossAmount,
                ProcessingFee = r.ProcessingFee,
                NetAmount = r.NetAmount,
                Currency = r.Currency,
                MatchStatus = r.MatchStatus,
            }).ToList());
        }

        /// <summary>
        /// Attaches a missing SettlementId to a WAITING gateway record.
        /// After the update, the reconciliation engine will pick this record up
        /// on the next Level-4 matching pass. Updates MatchStatus → PENDING.
        /// </summary>
        [HttpPatch("records/{id:guid}/settlement-id")]
        // WHY: RESOLVE is its own permission so that a MANAGER can attach missing settlement IDs
        // without owning full MANAGE. Backed by the AliasMap implication for legacy MANAGE grants.
        [RequirePermission("ADMIN.RECONCILIATION.RESOLVE")]
        public async Task<IActionResult> AttachSettlementId(
            Guid id,
            [FromBody] AttachSettlementIdRequest request,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(request.SettlementId))
                return BadRequest(new { message = "SettlementId is required." });

            var tenant = await _tenantContext.ResolveAsync(cancellationToken);
            if (tenant is null) return Unauthorized();

            await using var tenantDb = await _tenantDbContextFactory.CreateAsync(tenant.TenantId, cancellationToken);

            var record = await tenantDb.ImportedNormalizedRecords
                .FirstOrDefaultAsync(r => r.ImportedNormalizedRecordId == id, cancellationToken);

            if (record is null)
                return NotFound();

            if (record.MatchStatus != "WAITING")
                return Conflict(new { message = $"Record is not in WAITING status (current: {record.MatchStatus})." });

            record.SettlementId = request.SettlementId;
            record.MatchStatus = "PENDING"; // requeue for Level-4 re-run

            await tenantDb.SaveChangesAsync(cancellationToken);
            return NoContent();
        }

        // ─── Journal Posting ─────────────────────────────────────────────────────

        /// <summary>
        /// Returns all posted journal entries for this tenant.
        /// Filterable by transactionId or reconciliationMatchGroupId.
        /// </summary>
        [HttpGet("journal-entries")]
        [RequirePermission("ADMIN.JOURNAL.VIEW")]
        public async Task<ActionResult<List<JournalEntryResponse>>> GetJournalEntries(
            [FromQuery] Guid? transactionId = null,
            [FromQuery] Guid? matchGroupId = null,
            CancellationToken cancellationToken = default)
        {
            var tenant = await _tenantContext.ResolveAsync(cancellationToken);
            if (tenant is null) return Unauthorized();

            await using var tenantDb = await _tenantDbContextFactory.CreateAsync(tenant.TenantId, cancellationToken);

            var query = tenantDb.JournalEntries.AsNoTracking().AsQueryable();

            if (transactionId.HasValue)
                query = query.Where(j => j.TransactionId == transactionId.Value);

            if (matchGroupId.HasValue)
                query = query.Where(j => j.ReconciliationMatchGroupId == matchGroupId.Value);

            var entries = await query.OrderByDescending(j => j.PostedAt).ToListAsync(cancellationToken);

            return Ok(entries.Select(MapJournalToResponse).ToList());
        }

        /// <summary>
        /// Posts a journal entry for a JournalReady transaction (cashout path).
        /// Gate: transaction must be in JournalReady state.
        /// Idempotent guard: checks if a journal entry already exists for this transaction.
        /// </summary>
        [HttpPost("journal-entries/post-from-transaction/{transactionId:guid}")]
        // WHY: POST is more specific than MANAGE — a reviewer with JOURNAL.MANAGE legacy grants
        // still passes via the AliasMap implication in PermissionHandler.
        [RequirePermission("ADMIN.JOURNAL.POST")]
        public async Task<ActionResult<JournalEntryResponse>> PostJournalFromTransaction(
            Guid transactionId,
            [FromBody] PostJournalRequest request,
            CancellationToken cancellationToken = default)
        {
            var tenant = await _tenantContext.ResolveAsync(cancellationToken);
            if (tenant is null) return Unauthorized();

            await using var tenantDb = await _tenantDbContextFactory.CreateAsync(tenant.TenantId, cancellationToken);

            var transaction = await tenantDb.Transactions
                .FirstOrDefaultAsync(t => t.TransactionId == transactionId, cancellationToken);

            if (transaction is null)
                return NotFound(new { message = "Transaction not found." });

            if (transaction.TransactionState != TransactionState.JournalReady)
                return Conflict(new { message = $"Transaction must be in JournalReady state. Current: {transaction.TransactionState}." });

            // Idempotency guard: prevent double-posting.
            var existing = await tenantDb.JournalEntries
                .AsNoTracking()
                .AnyAsync(j => j.TransactionId == transactionId, cancellationToken);

            if (existing)
                return Conflict(new { message = "A journal entry has already been posted for this transaction." });

            var entry = new JournalEntry
            {
                JournalEntryId = Guid.NewGuid(),
                TransactionId = transactionId,
                EntryType = transaction.TransactionType == TransactionType.CashOut ? "CashOut" : "CashIn",
                Amount = transaction.Amount,
                Currency = "LKR",
                PostedAt = DateTime.UtcNow,
                PostedByUserId = _userContext.UserId,
                Notes = request.Notes,
            };

            tenantDb.JournalEntries.Add(entry);
            await tenantDb.SaveChangesAsync(cancellationToken);

            return CreatedAtAction(
                nameof(GetJournalEntries),
                new { transactionId = transactionId },
                MapJournalToResponse(entry));
        }

        /// <summary>
        /// Posts a fee-adjustment journal entry for a confirmed reconciliation match group (gateway path).
        /// Gate: match group must be confirmed (IsConfirmed = true) and not yet posted (IsJournalPosted = false).
        /// </summary>
        [HttpPost("match-groups/{id:guid}/post-journal")]
        [RequirePermission("ADMIN.JOURNAL.POST")]
        public async Task<ActionResult<JournalEntryResponse>> PostJournalFromMatchGroup(
            Guid id,
            [FromBody] PostJournalRequest request,
            CancellationToken cancellationToken = default)
        {
            var tenant = await _tenantContext.ResolveAsync(cancellationToken);
            if (tenant is null) return Unauthorized();

            await using var tenantDb = await _tenantDbContextFactory.CreateAsync(tenant.TenantId, cancellationToken);

            var group = await tenantDb.ReconciliationMatchGroups
                .Include(g => g.MatchedRecords)
                .FirstOrDefaultAsync(g => g.ReconciliationMatchGroupId == id, cancellationToken);

            if (group is null)
                return NotFound();

            if (!group.IsConfirmed)
                return Conflict(new { message = "Match group must be confirmed before posting a journal entry." });

            if (group.IsJournalPosted)
                return Conflict(new { message = "A journal entry has already been posted for this match group." });

            // Net amount = sum of MatchAmount across all members.
            var totalAmount = group.MatchedRecords.Sum(mr => mr.MatchAmount);

            var entry = new JournalEntry
            {
                JournalEntryId = Guid.NewGuid(),
                ReconciliationMatchGroupId = id,
                EntryType = "FeeAdjustment",
                Amount = totalAmount,
                Currency = "LKR",
                PostedAt = DateTime.UtcNow,
                PostedByUserId = _userContext.UserId,
                Notes = request.Notes,
            };

            group.IsJournalPosted = true;
            group.UpdatedAt = DateTime.UtcNow;

            tenantDb.JournalEntries.Add(entry);
            await tenantDb.SaveChangesAsync(cancellationToken);

            return CreatedAtAction(
                nameof(GetJournalEntries),
                new { matchGroupId = id },
                MapJournalToResponse(entry));
        }

        // ─── Private Mappers ─────────────────────────────────────────────────────

        private static ReconciliationMatchGroupResponse MapGroupToResponse(ReconciliationMatchGroup g) =>
            new()
            {
                ReconciliationMatchGroupId = g.ReconciliationMatchGroupId,
                ImportBatchId = g.ImportBatchId,
                MatchLevel = g.MatchLevel,
                SettlementKey = g.SettlementKey,
                IsConfirmed = g.IsConfirmed,
                ConfirmedByUserId = g.ConfirmedByUserId,
                ConfirmedAt = g.ConfirmedAt,
                IsJournalPosted = g.IsJournalPosted,
                CreatedAt = g.CreatedAt,
                UpdatedAt = g.UpdatedAt,
                MatchedRecords = g.MatchedRecords.Select(mr => new ReconciliationMatchedRecordResponse
                {
                    ReconciliationMatchedRecordId = mr.ReconciliationMatchedRecordId,
                    ImportedNormalizedRecordId = mr.ImportedNormalizedRecordId,
                    SourceType = mr.SourceType,
                    MatchAmount = mr.MatchAmount,
                    TransactionDate = mr.ImportedNormalizedRecord?.TransactionDate,
                    ReferenceNumber = mr.ImportedNormalizedRecord?.ReferenceNumber,
                    GrossAmount = mr.ImportedNormalizedRecord?.GrossAmount,
                    ProcessingFee = mr.ImportedNormalizedRecord?.ProcessingFee,
                    NetAmount = mr.ImportedNormalizedRecord?.NetAmount ?? 0,
                    Currency = mr.ImportedNormalizedRecord?.Currency ?? "LKR",
                    MatchStatus = mr.ImportedNormalizedRecord?.MatchStatus ?? "PENDING",
                }).ToList(),
            };

        private static JournalEntryResponse MapJournalToResponse(JournalEntry j) =>
            new()
            {
                JournalEntryId = j.JournalEntryId,
                TransactionId = j.TransactionId,
                ReconciliationMatchGroupId = j.ReconciliationMatchGroupId,
                EntryType = j.EntryType,
                Amount = j.Amount,
                Currency = j.Currency,
                PostedAt = j.PostedAt,
                PostedByUserId = j.PostedByUserId,
                Notes = j.Notes,
            };

        /// <summary>
        /// WHY: Loads the flat permission code list for the current user from the tenant DB.
        /// Used by SourceTypeScope to apply source-type restrictions after the coarse
        /// [RequirePermission] attribute has already succeeded.
        /// </summary>
        private async Task<IReadOnlyList<string>> GetUserPermissionsAsync(
            TenantDbContext tenantDb,
            CancellationToken cancellationToken = default)
        {
            if (_userContext.UserId is not { } userId)
                return Array.Empty<string>();

            return await tenantDb.UserRoles
                .AsNoTracking()
                .Where(ur => ur.UserId == userId && ur.Role.IsActive)
                .SelectMany(ur => ur.Role.RolePermissions.Select(rp => rp.Permission.Code))
                .Distinct()
                .ToListAsync(cancellationToken);
        }
    }
}
