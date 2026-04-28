using System.Security.Claims;
using finrecon360_backend.Authorization;
using finrecon360_backend.Dtos.Reconciliation;
using finrecon360_backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace finrecon360_backend.Controllers.Admin
{
    /// <summary>
    /// API endpoints for uploading and viewing bank statement imports.
    /// </summary>
    [ApiController]
    [Route("api/tenant-admin/reconciliation/imports")]
    [Authorize]
    [RequirePermission("ADMIN.IMPORT_WORKBENCH.VIEW")]
    public class BankStatementImportController : ControllerBase
    {
        private readonly IBankStatementImportService _bankStatementImportService;
        private readonly ILogger<BankStatementImportController> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="BankStatementImportController"/> class.
        /// </summary>
        /// <param name="bankStatementImportService">Bank statement import service dependency.</param>
        /// <param name="logger">Logger dependency.</param>
        public BankStatementImportController(
            IBankStatementImportService bankStatementImportService,
            ILogger<BankStatementImportController> logger)
        {
            _bankStatementImportService = bankStatementImportService;
            _logger = logger;
        }

        /// <summary>
        /// Uploads a bank statement file.
        /// </summary>
        /// <param name="request">Upload request (multipart/form-data).</param>
        /// <returns>Created statement import response.</returns>
        [HttpPost("upload")]
        [Consumes("multipart/form-data")]
        public async Task<ActionResult<StatementImportResponse>> Upload([FromForm] UploadStatementRequest request)
        {
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(userIdClaim, out var currentUserId))
            {
                _logger.LogWarning("Upload statement rejected because user identifier claim is missing or invalid.");
                return Unauthorized();
            }

            var created = await _bankStatementImportService.UploadStatementAsync(request, currentUserId);

            return CreatedAtAction(
                nameof(GetImportById),
                new { id = created.ImportId },
                created);
        }

        /// <summary>
        /// Gets paginated statement imports for a bank account.
        /// </summary>
        /// <param name="bankAccountId">Bank account identifier.</param>
        /// <param name="pageNumber">1-based page number.</param>
        /// <param name="pageSize">Number of items per page.</param>
        /// <returns>Paginated list of statement imports.</returns>
        [HttpGet]
        public async Task<ActionResult<PaginatedResponse<StatementImportResponse>>> GetImports(
            [FromQuery] Guid bankAccountId,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 20)
        {
            var result = await _bankStatementImportService.GetImportsAsync(bankAccountId, pageNumber, pageSize);
            return Ok(result);
        }

        /// <summary>
        /// Gets a statement import by id.
        /// </summary>
        /// <param name="id">Import identifier.</param>
        /// <returns>Import response if found.</returns>
        [HttpGet("{id:guid}")]
        public async Task<ActionResult<StatementImportResponse>> GetImportById(Guid id)
        {
            var result = await _bankStatementImportService.GetImportByIdAsync(id);
            if (result is null)
            {
                return NotFound();
            }

            return Ok(result);
        }
    }
}