using finrecon360_backend.Data;
using finrecon360_backend.Dtos.Reporting;
using finrecon360_backend.Models;
using Microsoft.EntityFrameworkCore;

namespace finrecon360_backend.Services.Reporting
{
    public interface IIncomeStatementService
    {
        Task<IncomeStatementResponse> GetAsync(TenantDbContext db, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);
    }

    /// <summary>
    /// Groups Trial-Balance-style net amounts by AccountType for Revenue/Expense accounts over a
    /// date range. Revenue accounts are credit-normal (negative under this ledger's sign
    /// convention), so they're negated for display as a positive figure; Expense accounts are
    /// debit-normal and already display positive as-is.
    /// </summary>
    public class IncomeStatementService : IIncomeStatementService
    {
        public async Task<IncomeStatementResponse> GetAsync(
            TenantDbContext db,
            DateTime fromUtc,
            DateTime toUtc,
            CancellationToken ct = default)
        {
            var balances = await db.JournalEntries
                .AsNoTracking()
                .Where(e => e.PostedAt >= fromUtc && e.PostedAt <= toUtc)
                .GroupBy(e => e.ChartOfAccountId)
                .Select(g => new { ChartOfAccountId = g.Key, Net = g.Sum(e => e.Amount) })
                .ToListAsync(ct);

            var accountsById = await db.ChartOfAccounts
                .AsNoTracking()
                .Where(a => a.AccountType == AccountType.Revenue || a.AccountType == AccountType.Expense)
                .ToDictionaryAsync(a => a.ChartOfAccountId, ct);

            var revenueLines = new List<IncomeStatementLineDto>();
            var expenseLines = new List<IncomeStatementLineDto>();
            var unclassified = 0m;

            foreach (var b in balances)
            {
                if (b.ChartOfAccountId is null)
                {
                    unclassified += b.Net;
                    continue;
                }

                if (!accountsById.TryGetValue(b.ChartOfAccountId.Value, out var account))
                {
                    // Resolves to a real account, but not one of type Revenue/Expense (e.g. Asset,
                    // Liability) — correctly out of scope for an income statement.
                    continue;
                }

                if (account.AccountType == AccountType.Revenue)
                {
                    revenueLines.Add(new IncomeStatementLineDto(account.Code, account.Name, -b.Net));
                }
                else
                {
                    expenseLines.Add(new IncomeStatementLineDto(account.Code, account.Name, b.Net));
                }
            }

            var orderedRevenue = revenueLines.OrderBy(l => l.AccountCode, StringComparer.Ordinal).ToList();
            var orderedExpense = expenseLines.OrderBy(l => l.AccountCode, StringComparer.Ordinal).ToList();
            var totalRevenue = orderedRevenue.Sum(l => l.Amount);
            var totalExpense = orderedExpense.Sum(l => l.Amount);

            return new IncomeStatementResponse(
                fromUtc,
                toUtc,
                orderedRevenue,
                orderedExpense,
                totalRevenue,
                totalExpense,
                totalRevenue - totalExpense,
                unclassified);
        }
    }
}
