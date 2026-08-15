using finrecon360_backend.Authorization;
using finrecon360_backend.Data;
using finrecon360_backend.Dtos.BankAccounts;
using finrecon360_backend.Services;
using finrecon360_backend.Services.BankAccounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace finrecon360_backend.Controllers.Admin
{
    [ApiController]
    [Route("api/admin/bank-accounts")]
    [Authorize]
    public class BankAccountsController : ControllerBase
    {
        private readonly AppDbContext _dbContext;
        private readonly ITenantContext _tenantContext;
        private readonly ITenantDbContextFactory _tenantDbContextFactory;
        private readonly IUserContext _userContext;
        private readonly BankAccountService _bankAccountService;

        public BankAccountsController(
            AppDbContext dbContext,
            ITenantContext tenantContext,
            ITenantDbContextFactory tenantDbContextFactory,
            IUserContext userContext,
            BankAccountService bankAccountService)
        {
            _dbContext = dbContext;
            _tenantContext = tenantContext;
            _tenantDbContextFactory = tenantDbContextFactory;
            _userContext = userContext;
            _bankAccountService = bankAccountService;
        }

        [HttpPost]
        [RequirePermission("ADMIN.BANK_ACCOUNTS.MANAGE")]
        public async Task<ActionResult<BankAccountResponse>> Create(
            [FromBody] CreateBankAccountRequest request,
            CancellationToken ct)
        {
            var auth = await AuthorizeTenantAdminAsync(ct);
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            try
            {
                var result = await _bankAccountService.CreateAsync(tenantDb, request, ct);
                return CreatedAtAction(nameof(GetById), new { id = result.BankAccountId }, result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet]
        [RequirePermission("ADMIN.BANK_ACCOUNTS.VIEW")]
        public async Task<ActionResult<List<BankAccountResponse>>> GetAll(CancellationToken ct)
        {
            var auth = await AuthorizeTenantAdminAsync(ct);
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            var result = await _bankAccountService.GetAllAsync(tenantDb, ct);
            return Ok(result);
        }

        [HttpGet("{id:guid}")]
        [RequirePermission("ADMIN.BANK_ACCOUNTS.VIEW")]
        public async Task<ActionResult<BankAccountResponse>> GetById(Guid id, CancellationToken ct)
        {
            var auth = await AuthorizeTenantAdminAsync(ct);
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            var result = await _bankAccountService.GetByIdAsync(tenantDb, id, ct);
            if (result == null)
            {
                return NotFound();
            }

            return Ok(result);
        }

        [HttpPut("{id:guid}")]
        [RequirePermission("ADMIN.BANK_ACCOUNTS.MANAGE")]
        public async Task<IActionResult> Update(
            Guid id,
            [FromBody] UpdateBankAccountRequest request,
            CancellationToken ct)
        {
            var auth = await AuthorizeTenantAdminAsync(ct);
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            try
            {
                var updated = await _bankAccountService.UpdateAsync(tenantDb, id, request, ct);
                if (!updated)
                {
                    return NotFound();
                }

                return NoContent();
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpDelete("{id:guid}")]
        [RequirePermission("ADMIN.BANK_ACCOUNTS.MANAGE")]
        public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
        {
            var auth = await AuthorizeTenantAdminAsync(ct);
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            var deactivated = await _bankAccountService.DeactivateAsync(tenantDb, id, ct);
            if (!deactivated)
            {
                return NotFound();
            }

            return NoContent();
        }

        private async Task<(TenantDbContext? Db, ActionResult? Error)> AuthorizeTenantAdminAsync(CancellationToken ct)
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
