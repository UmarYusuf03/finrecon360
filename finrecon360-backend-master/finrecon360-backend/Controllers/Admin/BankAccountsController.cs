using finrecon360_backend.Data;
using finrecon360_backend.Authorization;
using finrecon360_backend.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace finrecon360_backend.Controllers.Admin
{
    [ApiController]
    [Route("api/tenant-admin/bank-accounts")]
    [Authorize]
    [RequirePermission("ADMIN.IMPORT_WORKBENCH.VIEW")]
    public class BankAccountsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public BankAccountsController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<ActionResult<List<BankAccount>>> GetBankAccounts()
        {
            // By adding .IgnoreQueryFilters(), we tell Entity Framework to bypass 
            // the TenantId check just for this specific System Admin endpoint.
            return await _context.BankAccounts.IgnoreQueryFilters().ToListAsync();
        }
    }
}
