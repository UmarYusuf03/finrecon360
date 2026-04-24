using finrecon360_backend.Authorization;
using finrecon360_backend.Data;
using finrecon360_backend.Dtos.Transactions;
using finrecon360_backend.Services;
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
        private readonly AppDbContext _dbContext;
        private readonly ITenantContext _tenantContext;
        private readonly ITenantDbContextFactory _tenantDbContextFactory;
        private readonly IUserContext _userContext;
        private readonly TransactionService _transactionService;

        public TransactionsController(
            AppDbContext dbContext,
            ITenantContext tenantContext,
            ITenantDbContextFactory tenantDbContextFactory,
            IUserContext userContext,
            TransactionService transactionService)
        {
            _dbContext = dbContext;
            _tenantContext = tenantContext;
            _tenantDbContextFactory = tenantDbContextFactory;
            _userContext = userContext;
            _transactionService = transactionService;
        }

        [HttpPost]
        [RequirePermission("ADMIN.TRANSACTIONS.MANAGE")]
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
