using finrecon360_backend.Authorization;
using finrecon360_backend.Data;
using finrecon360_backend.Dtos.Reporting;
using finrecon360_backend.Services;
using finrecon360_backend.Services.Export;
using finrecon360_backend.Services.Reporting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace finrecon360_backend.Controllers.Admin
{
    // ── Export row shapes ────────────────────────────────────────────────────────────
    // Flattened, one-row-per-line views of the nested report DTOs — CSV/XLSX have no concept of
    // a nested "account with entries" structure, so exports flatten to a single table per report.

    public record GeneralLedgerExportRow(
        string AccountCode,
        string AccountName,
        DateTime PostedAt,
        string EntryType,
        string? Notes,
        decimal Amount,
        decimal RunningBalance);

    public record IncomeStatementExportRow(string Section, string AccountCode, string AccountName, decimal Amount);

    public record BalanceSheetExportRow(string Section, string AccountCode, string AccountName, decimal Amount);

    public record CashFlowExportRow(DateTime Date, decimal OpeningBalance, decimal CashIn, decimal CashOut, decimal ClosingBalance);

    [ApiController]
    [Route("api/admin/financial-reports")]
    [Authorize]
    [RequirePermission("ADMIN.FINANCIAL_REPORTS.VIEW")]
    public class FinancialReportsController : ControllerBase
    {
        private static readonly IReadOnlyList<ExportColumn<GeneralLedgerExportRow>> GeneralLedgerExportColumns = new List<ExportColumn<GeneralLedgerExportRow>>
        {
            new("Account Code", r => r.AccountCode),
            new("Account Name", r => r.AccountName),
            new("Posted At (UTC)", r => r.PostedAt.ToString("yyyy-MM-dd HH:mm:ss")),
            new("Entry Type", r => r.EntryType),
            new("Notes", r => r.Notes),
            new("Amount", r => r.Amount.ToString("0.00")),
            new("Running Balance", r => r.RunningBalance.ToString("0.00")),
        };

        private static readonly IReadOnlyList<ExportColumn<TrialBalanceLineDto>> TrialBalanceExportColumns = new List<ExportColumn<TrialBalanceLineDto>>
        {
            new("Account Code", l => l.AccountCode),
            new("Account Name", l => l.AccountName),
            new("Account Type", l => l.AccountType),
            new("Debit", l => l.Debit.ToString("0.00")),
            new("Credit", l => l.Credit.ToString("0.00")),
        };

        private static readonly IReadOnlyList<ExportColumn<IncomeStatementExportRow>> IncomeStatementExportColumns = new List<ExportColumn<IncomeStatementExportRow>>
        {
            new("Section", r => r.Section),
            new("Account Code", r => r.AccountCode),
            new("Account Name", r => r.AccountName),
            new("Amount", r => r.Amount.ToString("0.00")),
        };

        private static readonly IReadOnlyList<ExportColumn<BalanceSheetExportRow>> BalanceSheetExportColumns = new List<ExportColumn<BalanceSheetExportRow>>
        {
            new("Section", r => r.Section),
            new("Account Code", r => r.AccountCode),
            new("Account Name", r => r.AccountName),
            new("Amount", r => r.Amount.ToString("0.00")),
        };

        private static readonly IReadOnlyList<ExportColumn<CashFlowExportRow>> CashFlowExportColumns = new List<ExportColumn<CashFlowExportRow>>
        {
            new("Date", r => r.Date.ToString("yyyy-MM-dd")),
            new("Opening Balance", r => r.OpeningBalance.ToString("0.00")),
            new("Cash In", r => r.CashIn.ToString("0.00")),
            new("Cash Out", r => r.CashOut.ToString("0.00")),
            new("Closing Balance", r => r.ClosingBalance.ToString("0.00")),
        };

        private readonly AppDbContext _dbContext;
        private readonly ITenantContext _tenantContext;
        private readonly ITenantDbContextFactory _tenantDbContextFactory;
        private readonly IUserContext _userContext;
        private readonly IGeneralLedgerService _generalLedgerService;
        private readonly ITrialBalanceService _trialBalanceService;
        private readonly IIncomeStatementService _incomeStatementService;
        private readonly IBalanceSheetService _balanceSheetService;
        private readonly ICashFlowReportService _cashFlowReportService;
        private readonly IReportExporter _reportExporter;

        public FinancialReportsController(
            AppDbContext dbContext,
            ITenantContext tenantContext,
            ITenantDbContextFactory tenantDbContextFactory,
            IUserContext userContext,
            IGeneralLedgerService generalLedgerService,
            ITrialBalanceService trialBalanceService,
            IIncomeStatementService incomeStatementService,
            IBalanceSheetService balanceSheetService,
            ICashFlowReportService cashFlowReportService,
            IReportExporter reportExporter)
        {
            _dbContext = dbContext;
            _tenantContext = tenantContext;
            _tenantDbContextFactory = tenantDbContextFactory;
            _userContext = userContext;
            _generalLedgerService = generalLedgerService;
            _trialBalanceService = trialBalanceService;
            _incomeStatementService = incomeStatementService;
            _balanceSheetService = balanceSheetService;
            _cashFlowReportService = cashFlowReportService;
            _reportExporter = reportExporter;
        }

        [HttpGet("general-ledger")]
        public async Task<ActionResult<GeneralLedgerResponse>> GetGeneralLedger(
            [FromQuery] DateTime? fromUtc,
            [FromQuery] DateTime? toUtc,
            CancellationToken ct)
        {
            var auth = await AuthorizeTenantUserAsync(ct);
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            var range = ResolveGeneralLedgerRange(fromUtc, toUtc);
            if (range.Error != null) return range.Error;

            var result = await _generalLedgerService.GetAsync(tenantDb, range.From, range.To, ct);
            return Ok(result);
        }

        [HttpGet("general-ledger/export")]
        public async Task<IActionResult> ExportGeneralLedger(
            [FromQuery] string? format,
            [FromQuery] DateTime? fromUtc,
            [FromQuery] DateTime? toUtc,
            CancellationToken ct)
        {
            var auth = await AuthorizeTenantUserAsync(ct);
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            if (!_reportExporter.TryParseFormat(format, out var exportFormat))
            {
                return BadRequest(new { message = "Unsupported export format. Use 'csv' or 'xlsx'." });
            }

            var range = ResolveGeneralLedgerRange(fromUtc, toUtc);
            if (range.Error != null) return range.Error;

            var result = await _generalLedgerService.GetAsync(tenantDb, range.From, range.To, ct);
            var rows = result.Accounts
                .SelectMany(a => a.Entries.Select(e => new GeneralLedgerExportRow(
                    a.AccountCode, a.AccountName, e.PostedAt, e.EntryType, e.Notes, e.Amount, e.RunningBalance)))
                .ToList();

            if (rows.Count > _reportExporter.MaxRows)
            {
                return BadRequest(new { message = $"Export limited to {_reportExporter.MaxRows} rows. Narrow the date range and try again." });
            }

            var file = _reportExporter.Export(rows, GeneralLedgerExportColumns, "General Ledger", exportFormat);
            return File(file.Content, file.ContentType, $"general-ledger-{DateTime.UtcNow:yyyyMMddHHmmss}.{file.FileExtension}");
        }

        [HttpGet("trial-balance")]
        public async Task<ActionResult<TrialBalanceResponse>> GetTrialBalance(
            [FromQuery] DateTime? asOfUtc,
            CancellationToken ct)
        {
            var auth = await AuthorizeTenantUserAsync(ct);
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            var result = await _trialBalanceService.GetAsync(tenantDb, asOfUtc ?? DateTime.UtcNow, ct);
            return Ok(result);
        }

        [HttpGet("trial-balance/export")]
        public async Task<IActionResult> ExportTrialBalance(
            [FromQuery] string? format,
            [FromQuery] DateTime? asOfUtc,
            CancellationToken ct)
        {
            var auth = await AuthorizeTenantUserAsync(ct);
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            if (!_reportExporter.TryParseFormat(format, out var exportFormat))
            {
                return BadRequest(new { message = "Unsupported export format. Use 'csv' or 'xlsx'." });
            }

            var result = await _trialBalanceService.GetAsync(tenantDb, asOfUtc ?? DateTime.UtcNow, ct);
            if (result.Lines.Count > _reportExporter.MaxRows)
            {
                return BadRequest(new { message = $"Export limited to {_reportExporter.MaxRows} rows." });
            }

            var file = _reportExporter.Export(result.Lines, TrialBalanceExportColumns, "Trial Balance", exportFormat);
            return File(file.Content, file.ContentType, $"trial-balance-{DateTime.UtcNow:yyyyMMddHHmmss}.{file.FileExtension}");
        }

        [HttpGet("income-statement")]
        public async Task<ActionResult<IncomeStatementResponse>> GetIncomeStatement(
            [FromQuery] DateTime? fromUtc,
            [FromQuery] DateTime? toUtc,
            CancellationToken ct)
        {
            var auth = await AuthorizeTenantUserAsync(ct);
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            var range = ResolveIncomeStatementRange(fromUtc, toUtc);
            if (range.Error != null) return range.Error;

            var result = await _incomeStatementService.GetAsync(tenantDb, range.From, range.To, ct);
            return Ok(result);
        }

        [HttpGet("income-statement/export")]
        public async Task<IActionResult> ExportIncomeStatement(
            [FromQuery] string? format,
            [FromQuery] DateTime? fromUtc,
            [FromQuery] DateTime? toUtc,
            CancellationToken ct)
        {
            var auth = await AuthorizeTenantUserAsync(ct);
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            if (!_reportExporter.TryParseFormat(format, out var exportFormat))
            {
                return BadRequest(new { message = "Unsupported export format. Use 'csv' or 'xlsx'." });
            }

            var range = ResolveIncomeStatementRange(fromUtc, toUtc);
            if (range.Error != null) return range.Error;

            var result = await _incomeStatementService.GetAsync(tenantDb, range.From, range.To, ct);
            var rows = result.RevenueLines.Select(l => new IncomeStatementExportRow("Revenue", l.AccountCode, l.AccountName, l.Amount))
                .Concat(result.ExpenseLines.Select(l => new IncomeStatementExportRow("Expense", l.AccountCode, l.AccountName, l.Amount)))
                .ToList();

            if (rows.Count > _reportExporter.MaxRows)
            {
                return BadRequest(new { message = $"Export limited to {_reportExporter.MaxRows} rows." });
            }

            var file = _reportExporter.Export(rows, IncomeStatementExportColumns, "Income Statement", exportFormat);
            return File(file.Content, file.ContentType, $"income-statement-{DateTime.UtcNow:yyyyMMddHHmmss}.{file.FileExtension}");
        }

        [HttpGet("balance-sheet")]
        public async Task<ActionResult<BalanceSheetResponse>> GetBalanceSheet(
            [FromQuery] DateTime? asOfUtc,
            CancellationToken ct)
        {
            var auth = await AuthorizeTenantUserAsync(ct);
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            var result = await _balanceSheetService.GetAsync(tenantDb, asOfUtc ?? DateTime.UtcNow, ct);
            return Ok(result);
        }

        [HttpGet("balance-sheet/export")]
        public async Task<IActionResult> ExportBalanceSheet(
            [FromQuery] string? format,
            [FromQuery] DateTime? asOfUtc,
            CancellationToken ct)
        {
            var auth = await AuthorizeTenantUserAsync(ct);
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            if (!_reportExporter.TryParseFormat(format, out var exportFormat))
            {
                return BadRequest(new { message = "Unsupported export format. Use 'csv' or 'xlsx'." });
            }

            var result = await _balanceSheetService.GetAsync(tenantDb, asOfUtc ?? DateTime.UtcNow, ct);
            var rows = result.AssetLines.Select(l => new BalanceSheetExportRow("Asset", l.AccountCode, l.AccountName, l.Amount))
                .Concat(result.LiabilityLines.Select(l => new BalanceSheetExportRow("Liability", l.AccountCode, l.AccountName, l.Amount)))
                .Concat(result.EquityLines.Select(l => new BalanceSheetExportRow("Equity", l.AccountCode, l.AccountName, l.Amount)))
                .ToList();

            if (rows.Count > _reportExporter.MaxRows)
            {
                return BadRequest(new { message = $"Export limited to {_reportExporter.MaxRows} rows." });
            }

            var file = _reportExporter.Export(rows, BalanceSheetExportColumns, "Balance Sheet", exportFormat);
            return File(file.Content, file.ContentType, $"balance-sheet-{DateTime.UtcNow:yyyyMMddHHmmss}.{file.FileExtension}");
        }

        [HttpGet("cash-flow")]
        public async Task<ActionResult<CashFlowResponse>> GetCashFlow(
            [FromQuery] DateTime? fromUtc,
            [FromQuery] DateTime? toUtc,
            CancellationToken ct)
        {
            var auth = await AuthorizeTenantUserAsync(ct);
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            var range = ResolveGeneralLedgerRange(fromUtc, toUtc);
            if (range.Error != null) return range.Error;

            var result = await _cashFlowReportService.GetAsync(tenantDb, range.From, range.To, ct);
            return Ok(result);
        }

        [HttpGet("cash-flow/export")]
        public async Task<IActionResult> ExportCashFlow(
            [FromQuery] string? format,
            [FromQuery] DateTime? fromUtc,
            [FromQuery] DateTime? toUtc,
            CancellationToken ct)
        {
            var auth = await AuthorizeTenantUserAsync(ct);
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            if (!_reportExporter.TryParseFormat(format, out var exportFormat))
            {
                return BadRequest(new { message = "Unsupported export format. Use 'csv' or 'xlsx'." });
            }

            var range = ResolveGeneralLedgerRange(fromUtc, toUtc);
            if (range.Error != null) return range.Error;

            var result = await _cashFlowReportService.GetAsync(tenantDb, range.From, range.To, ct);
            var rows = result.Days
                .Select(d => new CashFlowExportRow(d.Date, d.OpeningBalance, d.CashIn, d.CashOut, d.ClosingBalance))
                .ToList();

            if (rows.Count > _reportExporter.MaxRows)
            {
                return BadRequest(new { message = $"Export limited to {_reportExporter.MaxRows} rows. Narrow the date range and try again." });
            }

            var file = _reportExporter.Export(rows, CashFlowExportColumns, "Cash Flow", exportFormat);
            return File(file.Content, file.ContentType, $"cash-flow-{DateTime.UtcNow:yyyyMMddHHmmss}.{file.FileExtension}");
        }

        private static (DateTime From, DateTime To, ActionResult? Error) ResolveGeneralLedgerRange(DateTime? fromUtc, DateTime? toUtc)
        {
            var to = toUtc ?? DateTime.UtcNow;
            var from = fromUtc ?? to.AddDays(-30);
            if (from > to)
            {
                return (from, to, new BadRequestObjectResult(new { message = "fromUtc must be before or equal to toUtc." }));
            }

            return (from, to, null);
        }

        private static (DateTime From, DateTime To, ActionResult? Error) ResolveIncomeStatementRange(DateTime? fromUtc, DateTime? toUtc)
        {
            var to = toUtc ?? DateTime.UtcNow;
            var from = fromUtc ?? new DateTime(to.Year, to.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            if (from > to)
            {
                return (from, to, new BadRequestObjectResult(new { message = "fromUtc must be before or equal to toUtc." }));
            }

            return (from, to, null);
        }

        private async Task<(TenantDbContext? Db, ActionResult? Error)> AuthorizeTenantUserAsync(CancellationToken ct)
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
