using finrecon360_backend.Data;
using finrecon360_backend.Dtos.Reporting;
using finrecon360_backend.Models;
using Microsoft.EntityFrameworkCore;

namespace finrecon360_backend.Services.Reporting
{
    public interface IBalanceSheetService
    {
        Task<BalanceSheetResponse> GetAsync(TenantDbContext db, DateTime asOfUtc, CancellationToken ct = default);
    }

    /// <summary>
    /// Groups Trial-Balance-style net amounts by AccountType for Asset/Liability/Equity accounts
    /// as of a date. Asset accounts are debit-normal and display positive as-is; Liability/Equity
    /// accounts are credit-normal (negative under this ledger's sign convention) and are negated
    /// for display.
    ///
    /// Revenue and Expense accounts have no balance-sheet section of their own, but their net
    /// activity up to asOfUtc is real ledger data and can't just be dropped without breaking
    /// Assets == Liabilities + Equity. It's rolled up into a single synthetic "Retained Earnings"
    /// equity line (RetainedEarningsAccount) — the standard accounting treatment for a ledger
    /// with no separate period-close/retained-earnings posting step.
    /// </summary>
    public class BalanceSheetService : IBalanceSheetService
    {
        public async Task<BalanceSheetResponse> GetAsync(
            TenantDbContext db,
            DateTime asOfUtc,
            CancellationToken ct = default)
        {
            var balances = await db.JournalEntries
                .AsNoTracking()
                .Where(e => e.PostedAt <= asOfUtc)
                .GroupBy(e => e.ChartOfAccountId)
                .Select(g => new { ChartOfAccountId = g.Key, Net = g.Sum(e => e.Amount) })
                .ToListAsync(ct);

            var accountsById = await db.ChartOfAccounts
                .AsNoTracking()
                .ToDictionaryAsync(a => a.ChartOfAccountId, ct);

            var assetLines = new List<BalanceSheetLineDto>();
            var liabilityLines = new List<BalanceSheetLineDto>();
            var equityLines = new List<BalanceSheetLineDto>();
            var unclassified = 0m;
            var retainedEarningsNet = 0m;
            var hasRevenueOrExpenseActivity = false;

            foreach (var b in balances)
            {
                if (b.ChartOfAccountId is null)
                {
                    unclassified += b.Net;
                    continue;
                }

                if (!accountsById.TryGetValue(b.ChartOfAccountId.Value, out var account))
                {
                    // Journal entry references a ChartOfAccountId that no longer resolves to a
                    // row (e.g. deleted account) — treat like Unclassified rather than dropping it.
                    unclassified += b.Net;
                    continue;
                }

                switch (account.AccountType)
                {
                    case AccountType.Asset:
                        assetLines.Add(new BalanceSheetLineDto(account.Code, account.Name, b.Net));
                        break;
                    case AccountType.Liability:
                        liabilityLines.Add(new BalanceSheetLineDto(account.Code, account.Name, -b.Net));
                        break;
                    case AccountType.Equity:
                        equityLines.Add(new BalanceSheetLineDto(account.Code, account.Name, -b.Net));
                        break;
                    case AccountType.Revenue:
                    case AccountType.Expense:
                        // Both accumulate here as raw signed Net; the single -retainedEarningsNet
                        // negation below applies the same credit-normal display convention as
                        // Liability/Equity to their combined effect (revenue and expense already
                        // carry opposite raw signs, so summing raw Net before negating is correct).
                        retainedEarningsNet += b.Net;
                        hasRevenueOrExpenseActivity = true;
                        break;
                }
            }

            var orderedAssets = assetLines.OrderBy(l => l.AccountCode, StringComparer.Ordinal).ToList();
            var orderedLiabilities = liabilityLines.OrderBy(l => l.AccountCode, StringComparer.Ordinal).ToList();
            var orderedEquity = equityLines.OrderBy(l => l.AccountCode, StringComparer.Ordinal).ToList();

            if (hasRevenueOrExpenseActivity)
            {
                orderedEquity.Add(new BalanceSheetLineDto(
                    RetainedEarningsAccount.Code, RetainedEarningsAccount.Name, -retainedEarningsNet));
            }

            return new BalanceSheetResponse(
                asOfUtc,
                orderedAssets,
                orderedLiabilities,
                orderedEquity,
                orderedAssets.Sum(l => l.Amount),
                orderedLiabilities.Sum(l => l.Amount),
                orderedEquity.Sum(l => l.Amount),
                unclassified);
        }
    }
}
