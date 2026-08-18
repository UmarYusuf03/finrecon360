using finrecon360_backend.Authorization;
using finrecon360_backend.Data;
using finrecon360_backend.Dtos.Transactions;
using finrecon360_backend.Services;
using finrecon360_backend.Services.Export;
using finrecon360_backend.Services.Transactions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace finrecon360_backend.Controllers.Admin
{
    [ApiController]
    [Route("api/admin/transactions")]
    [Authorize]
    public class TransactionsController : ControllerBase
    {
        private static readonly IReadOnlyList<ExportColumn<TransactionResponse>> ExportColumns = new List<ExportColumn<TransactionResponse>>
        {
            new("Date", t => t.TransactionDate.ToString("yyyy-MM-dd")),
            new("Reference", t => t.ReferenceNumber),
            new("Description", t => t.Description),
            new("Amount", t => t.Amount.ToString("0.00")),
            new("Type", t => t.TransactionType),
            new("Method", t => t.PaymentMethod),
            new("State", t => t.TransactionState),
            new("Bank Account ID", t => t.BankAccountId?.ToString()),
            new("Created At (UTC)", t => t.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")),
            new("Approved At (UTC)", t => t.ApprovedAt?.ToString("yyyy-MM-dd HH:mm:ss")),
            new("Rejected At (UTC)", t => t.RejectedAt?.ToString("yyyy-MM-dd HH:mm:ss")),
            new("Rejection Reason", t => t.RejectionReason),
        };

        private readonly AppDbContext _dbContext;
        private readonly ITenantContext _tenantContext;
        private readonly ITenantDbContextFactory _tenantDbContextFactory;
        private readonly IUserContext _userContext;
        private readonly TransactionService _transactionService;
        private readonly IReportExporter _reportExporter;

        public TransactionsController(
            AppDbContext dbContext,
            ITenantContext tenantContext,
            ITenantDbContextFactory tenantDbContextFactory,
            IUserContext userContext,
            TransactionService transactionService,
            IReportExporter reportExporter)
        {
            _dbContext = dbContext;
            _tenantContext = tenantContext;
            _tenantDbContextFactory = tenantDbContextFactory;
            _userContext = userContext;
            _transactionService = transactionService;
            _reportExporter = reportExporter;
        }

        [HttpPost]
        [RequirePermission("ADMIN.TRANSACTIONS.CREATE")]
        public async Task<ActionResult<TransactionResponse>> Create(
            [FromBody] CreateTransactionRequest request,
            CancellationToken ct)
        {
            var auth = await AuthorizeTenantUserAsync(ct);
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            try
            {
                var result = await _transactionService.CreateAsync(tenantDb, request, auth.UserId!.Value, ct);
                return CreatedAtAction(nameof(GetById), new { id = result.TransactionId }, result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet]
        [RequirePermission("ADMIN.TRANSACTIONS.VIEW")]
        public async Task<ActionResult<List<TransactionResponse>>> GetAll(CancellationToken ct)
        {
            var auth = await AuthorizeTenantUserAsync(ct);
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            var result = await _transactionService.GetAllAsync(tenantDb, ct);
            return Ok(result);
        }

        [HttpGet("export")]
        [RequirePermission("ADMIN.TRANSACTIONS.VIEW")]
        public async Task<IActionResult> Export(
            [FromQuery] string? format,
            [FromQuery] string? state,
            [FromQuery] string? search,
            CancellationToken ct)
        {
            var auth = await AuthorizeTenantUserAsync(ct);
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            if (!_reportExporter.TryParseFormat(format, out var exportFormat))
            {
                return BadRequest(new { message = "Unsupported export format. Use 'csv' or 'xlsx'." });
            }

            var all = await _transactionService.GetAllAsync(tenantDb, ct);
            var filtered = ApplyExportFilters(all, state, search);

            if (filtered.Count > _reportExporter.MaxRows)
            {
                return BadRequest(new
                {
                    message = $"Export limited to {_reportExporter.MaxRows} rows. Narrow the state or search filters and try again."
                });
            }

            var file = _reportExporter.Export(filtered, ExportColumns, "Transactions", exportFormat);
            return File(file.Content, file.ContentType, $"transactions-{DateTime.UtcNow:yyyyMMddHHmmss}.{file.FileExtension}");
        }

        // Mirrors the client-side search/state filtering in admin-transactions.ts so the export
        // matches exactly what the user sees on screen.
        private static List<TransactionResponse> ApplyExportFilters(
            List<TransactionResponse> transactions,
            string? state,
            string? search)
        {
            IEnumerable<TransactionResponse> query = transactions;

            if (!string.IsNullOrWhiteSpace(state) && !string.Equals(state, "All", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(t => string.Equals(t.TransactionState, state, StringComparison.OrdinalIgnoreCase));
            }

            var term = search?.Trim();
            if (!string.IsNullOrEmpty(term))
            {
                query = query.Where(t =>
                    (t.Description?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (t.ReferenceNumber?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    t.TransactionType.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    t.PaymentMethod.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    t.TransactionState.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    t.Amount.ToString("0.00").Contains(term, StringComparison.OrdinalIgnoreCase));
            }

            return query.ToList();
        }

        [HttpGet("journal-ready")]
        [RequirePermission("ADMIN.TRANSACTIONS.VIEW")]
        public async Task<ActionResult<List<TransactionResponse>>> GetJournalReady(CancellationToken ct)
        {
            var auth = await AuthorizeTenantUserAsync(ct);
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            var result = await _transactionService.GetJournalReadyAsync(tenantDb, ct);
            return Ok(result);
        }

        [HttpGet("needs-bank-match")]
        [RequirePermission("ADMIN.TRANSACTIONS.VIEW")]
        public async Task<ActionResult<List<TransactionResponse>>> GetNeedsBankMatch(CancellationToken ct)
        {
            var auth = await AuthorizeTenantUserAsync(ct);
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            var result = await _transactionService.GetNeedsBankMatchAsync(tenantDb, ct);
            return Ok(result);
        }

        [HttpGet("{id:guid}")]
        [RequirePermission("ADMIN.TRANSACTIONS.VIEW")]
        public async Task<ActionResult<TransactionResponse>> GetById(Guid id, CancellationToken ct)
        {
            var auth = await AuthorizeTenantUserAsync(ct);
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            var result = await _transactionService.GetByIdAsync(tenantDb, id, ct);
            if (result == null)
            {
                return NotFound();
            }

            return Ok(result);
        }

        // IMPORTANT:
        // Once a transaction is approved or rejected, it becomes immutable.
        // This ensures audit integrity for financial records.
        // Any corrections must be handled through reversal or adjustment transactions.
        [HttpPut("{id:guid}")]
        [RequirePermission("ADMIN.TRANSACTIONS.MANAGE")]
        public async Task<ActionResult<TransactionResponse>> Update(
            Guid id,
            [FromBody] UpdateTransactionRequest request,
            CancellationToken ct)
        {
            var auth = await AuthorizeTenantUserAsync(ct);
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            try
            {
                var result = await _transactionService.UpdateAsync(tenantDb, id, request, auth.UserId!.Value, ct);
                if (result == null)
                {
                    return NotFound();
                }

                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("{id:guid}/history")]
        [RequirePermission("ADMIN.TRANSACTIONS.VIEW")]
        public async Task<ActionResult<List<TransactionStateHistoryResponse>>> GetHistory(Guid id, CancellationToken ct)
        {
            var auth = await AuthorizeTenantUserAsync(ct);
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            var transaction = await _transactionService.GetByIdAsync(tenantDb, id, ct);
            if (transaction == null)
            {
                return NotFound();
            }

            var result = await _transactionService.GetHistoryAsync(tenantDb, id, ct);
            return Ok(result);
        }

        // IMPORTANT:
        // Once a transaction is approved or rejected, it becomes immutable.
        // This ensures audit integrity for financial records.
        // Any corrections must be handled through reversal or adjustment transactions.
        [HttpPost("{id:guid}/approve")]
        [RequirePermission("ADMIN.TRANSACTIONS.MANAGE")]
        public async Task<ActionResult<TransactionResponse>> Approve(
            Guid id,
            [FromBody] ApproveTransactionRequest request,
            CancellationToken ct)
        {
            var auth = await AuthorizeTenantUserAsync(ct);
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            try
            {
                var result = await _transactionService.ApproveAsync(tenantDb, id, auth.UserId!.Value, request, ct);
                if (result == null)
                {
                    return NotFound();
                }

                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // IMPORTANT:
        // Once a transaction is approved or rejected, it becomes immutable.
        // This ensures audit integrity for financial records.
        // Any corrections must be handled through reversal or adjustment transactions.
        [HttpPost("{id:guid}/reject")]
        [RequirePermission("ADMIN.TRANSACTIONS.MANAGE")]
        public async Task<ActionResult<TransactionResponse>> Reject(
            Guid id,
            [FromBody] RejectTransactionRequest request,
            CancellationToken ct)
        {
            var auth = await AuthorizeTenantUserAsync(ct);
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            try
            {
                var result = await _transactionService.RejectAsync(tenantDb, id, auth.UserId!.Value, request, ct);
                if (result == null)
                {
                    return NotFound();
                }

                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        private async Task<(TenantDbContext? Db, Guid? UserId, ActionResult? Error)> AuthorizeTenantUserAsync(CancellationToken ct)
        {
            if (_userContext.UserId is not { } userId) return (null, null, Unauthorized());

            var tenant = await _tenantContext.ResolveAsync(ct);
            if (tenant == null) return (null, null, Forbid());

            var isTenantMember = await _dbContext.TenantUsers.AsNoTracking()
                .AnyAsync(tu => tu.TenantId == tenant.TenantId && tu.UserId == userId, ct);
            if (!isTenantMember) return (null, null, Forbid());

            // Tenant operational data is per-database, so resolve and open the tenant DB per request.
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
