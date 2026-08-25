using finrecon360_backend.Data;
using finrecon360_backend.Dtos.Reporting;
using finrecon360_backend.Models;
using Microsoft.EntityFrameworkCore;

namespace finrecon360_backend.Services.Reporting
{
    public interface ICashFlowReportService
    {
        Task<CashFlowResponse> GetAsync(TenantDbContext db, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);
    }

    /// <summary>
    /// Historical/actual cash flow: day-by-day cash in/out and running balance over JournalEntry
    /// rows posted to Asset-type ("cash") accounts. Unclassified (null ChartOfAccountId) entries
    /// count toward the daily totals and running balance the same way a mapped Asset entry would —
    /// a cash account is defined by what actually happened to cash, not by whether someone
    /// remembered to map an account code — with their net amount additionally disclosed via
    /// UnclassifiedAmount so the gap stays visible (same convention as
    /// IncomeStatementService/BalanceSheetService's UnclassifiedAmount).
    ///
    /// Not to be confused with CashFlowForecastService, which projects upcoming settlements
    /// forward; this reports what has already posted.
    /// </summary>
    public class CashFlowReportService : ICashFlowReportService
    {
        public async Task<CashFlowResponse> GetAsync(
            TenantDbContext db,
            DateTime fromUtc,
            DateTime toUtc,
            CancellationToken ct = default)
        {
            var assetAccountIds = await db.ChartOfAccounts
                .AsNoTracking()
                .Where(a => a.AccountType == AccountType.Asset)
                .Select(a => a.ChartOfAccountId)
                .ToListAsync(ct);

            var openingBalance = await db.JournalEntries
                .AsNoTracking()
                .Where(e => e.PostedAt < fromUtc && (e.ChartOfAccountId == null || assetAccountIds.Contains(e.ChartOfAccountId!.Value)))
                .Select(e => e.Amount)
                .ToListAsync(ct);

            var rangeEntries = await db.JournalEntries
                .AsNoTracking()
                .Where(e => e.PostedAt >= fromUtc && e.PostedAt <= toUtc && (e.ChartOfAccountId == null || assetAccountIds.Contains(e.ChartOfAccountId!.Value)))
                .OrderBy(e => e.PostedAt)
                .ToListAsync(ct);

            var unclassifiedAmount = rangeEntries.Where(e => e.ChartOfAccountId == null).Sum(e => e.Amount);
            var entriesByDay = rangeEntries.ToLookup(e => e.PostedAt.Date);

            var days = new List<CashFlowDayDto>();
            var running = openingBalance.Sum();

            for (var date = fromUtc.Date; date <= toUtc.Date; date = date.AddDays(1))
            {
                var dayEntries = entriesByDay[date];
                var cashIn = dayEntries.Where(e => e.Amount > 0).Sum(e => e.Amount);
                var cashOut = dayEntries.Where(e => e.Amount < 0).Sum(e => -e.Amount);

                var opening = running;
                running += cashIn - cashOut;
                days.Add(new CashFlowDayDto(date, opening, cashIn, cashOut, running));
            }

            var totalCashIn = days.Sum(d => d.CashIn);
            var totalCashOut = days.Sum(d => d.CashOut);

            return new CashFlowResponse(fromUtc, toUtc, days, totalCashIn, totalCashOut, totalCashIn - totalCashOut, unclassifiedAmount);
        }
    }
}
