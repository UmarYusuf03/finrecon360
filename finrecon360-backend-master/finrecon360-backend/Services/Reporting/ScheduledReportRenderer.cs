using finrecon360_backend.Data;
using finrecon360_backend.Dtos.Reporting;
using finrecon360_backend.Services.Export;
using Microsoft.EntityFrameworkCore;

namespace finrecon360_backend.Services.Reporting
{
    /// <summary>
    /// Renders a named report type (as stored on ReportSchedule.ReportType) to an export file,
    /// for ReportScheduleHostedService to email out. Each report uses a fixed, sensible default
    /// range appropriate to a weekly cadence — schedules don't carry their own date range, only a
    /// report type and delivery day.
    /// </summary>
    public interface IScheduledReportRenderer
    {
        /// <summary>Recognized values: TrialBalance, IncomeStatement, BalanceSheet, CashFlow, ReconciliationTrend.</summary>
        bool IsKnownReportType(string reportType);

        Task<ExportFile> RenderAsync(
            TenantDbContext db,
            string reportType,
            ReportExportFormat format,
            CancellationToken ct = default);
    }

    public class ScheduledReportRenderer : IScheduledReportRenderer
    {
        private static readonly IReadOnlyList<ExportColumn<TrialBalanceLineDto>> TrialBalanceColumns = new List<ExportColumn<TrialBalanceLineDto>>
        {
            new("Account Code", l => l.AccountCode),
            new("Account Name", l => l.AccountName),
            new("Account Type", l => l.AccountType),
            new("Debit", l => l.Debit.ToString("0.00")),
            new("Credit", l => l.Credit.ToString("0.00")),
        };

        private static readonly IReadOnlyList<ExportColumn<(string Section, string AccountCode, string AccountName, decimal Amount)>> SectionedLineColumns =
            new List<ExportColumn<(string Section, string AccountCode, string AccountName, decimal Amount)>>
            {
                new("Section", r => r.Section),
                new("Account Code", r => r.AccountCode),
                new("Account Name", r => r.AccountName),
                new("Amount", r => r.Amount.ToString("0.00")),
            };

        private static readonly IReadOnlyList<ExportColumn<ReconciliationTrendDayDto>> TrendColumns = new List<ExportColumn<ReconciliationTrendDayDto>>
        {
            new("Date", d => d.SnapshotDate.ToString("yyyy-MM-dd")),
            new("Match Level", d => d.MatchLevel),
            new("Matched", d => d.MatchedCount.ToString()),
            new("Confirmed", d => d.ConfirmedCount.ToString()),
            new("Exceptions", d => d.ExceptionCount.ToString()),
            new("Unmatched", d => d.UnmatchedCount.ToString()),
            new("Avg Time To Match (hrs)", d => d.AverageTimeToMatchHours?.ToString("0.00")),
        };

        private static readonly IReadOnlyList<ExportColumn<CashFlowDayDto>> CashFlowColumns = new List<ExportColumn<CashFlowDayDto>>
        {
            new("Date", d => d.Date.ToString("yyyy-MM-dd")),
            new("Opening Balance", d => d.OpeningBalance.ToString("0.00")),
            new("Cash In", d => d.CashIn.ToString("0.00")),
            new("Cash Out", d => d.CashOut.ToString("0.00")),
            new("Closing Balance", d => d.ClosingBalance.ToString("0.00")),
        };

        private readonly ITrialBalanceService _trialBalanceService;
        private readonly IIncomeStatementService _incomeStatementService;
        private readonly IBalanceSheetService _balanceSheetService;
        private readonly ICashFlowReportService _cashFlowReportService;
        private readonly IReportExporter _reportExporter;

        public ScheduledReportRenderer(
            ITrialBalanceService trialBalanceService,
            IIncomeStatementService incomeStatementService,
            IBalanceSheetService balanceSheetService,
            ICashFlowReportService cashFlowReportService,
            IReportExporter reportExporter)
        {
            _trialBalanceService = trialBalanceService;
            _incomeStatementService = incomeStatementService;
            _balanceSheetService = balanceSheetService;
            _cashFlowReportService = cashFlowReportService;
            _reportExporter = reportExporter;
        }

        public bool IsKnownReportType(string reportType) => reportType switch
        {
            "TrialBalance" or "IncomeStatement" or "BalanceSheet" or "CashFlow" or "ReconciliationTrend" => true,
            _ => false,
        };

        public async Task<ExportFile> RenderAsync(
            TenantDbContext db,
            string reportType,
            ReportExportFormat format,
            CancellationToken ct = default)
        {
            var now = DateTime.UtcNow;
            var weekAgo = now.AddDays(-7);

            switch (reportType)
            {
                case "TrialBalance":
                {
                    var report = await _trialBalanceService.GetAsync(db, now, ct);
                    return _reportExporter.Export(report.Lines, TrialBalanceColumns, "Trial Balance", format);
                }
                case "IncomeStatement":
                {
                    var report = await _incomeStatementService.GetAsync(db, weekAgo, now, ct);
                    var rows = report.RevenueLines.Select(l => ("Revenue", l.AccountCode, l.AccountName, l.Amount))
                        .Concat(report.ExpenseLines.Select(l => ("Expense", l.AccountCode, l.AccountName, l.Amount)))
                        .ToList();
                    return _reportExporter.Export(rows, SectionedLineColumns, "Income Statement", format);
                }
                case "BalanceSheet":
                {
                    var report = await _balanceSheetService.GetAsync(db, now, ct);
                    var rows = report.AssetLines.Select(l => ("Asset", l.AccountCode, l.AccountName, l.Amount))
                        .Concat(report.LiabilityLines.Select(l => ("Liability", l.AccountCode, l.AccountName, l.Amount)))
                        .Concat(report.EquityLines.Select(l => ("Equity", l.AccountCode, l.AccountName, l.Amount)))
                        .ToList();
                    return _reportExporter.Export(rows, SectionedLineColumns, "Balance Sheet", format);
                }
                case "CashFlow":
                {
                    var report = await _cashFlowReportService.GetAsync(db, weekAgo, now, ct);
                    return _reportExporter.Export(report.Days, CashFlowColumns, "Cash Flow", format);
                }
                case "ReconciliationTrend":
                {
                    var days = await db.ReconciliationDailySnapshots
                        .Where(s => s.SnapshotDate >= weekAgo.Date && s.SnapshotDate <= now.Date)
                        .OrderBy(s => s.SnapshotDate)
                        .ThenBy(s => s.MatchLevel)
                        .Select(s => new ReconciliationTrendDayDto(
                            s.SnapshotDate, s.MatchLevel, s.MatchedCount, s.ConfirmedCount,
                            s.ExceptionCount, s.UnmatchedCount, s.AverageTimeToMatchHours))
                        .ToListAsync(ct);
                    return _reportExporter.Export(days, TrendColumns, "Reconciliation Trend", format);
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(reportType), reportType, "Unknown scheduled report type.");
            }
        }
    }
}
