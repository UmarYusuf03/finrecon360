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
    /// Deliberately does NOT assert Assets == Liabilities + Equity: this ledger has no
    /// retained-earnings roll-up from the income statement into Equity, so that identity does not
    /// hold today by design, not by data-integrity bug — asserting it here would be misleading.
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
                .Where(a => a.AccountType == AccountType.Asset
                    || a.AccountType == AccountType.Liability
                    || a.AccountType == AccountType.Equity)
                .ToDictionaryAsync(a => a.ChartOfAccountId, ct);

            var assetLines = new List<BalanceSheetLineDto>();
            var liabilityLines = new List<BalanceSheetLineDto>();
            var equityLines = new List<BalanceSheetLineDto>();
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
                    // Resolves to a real account, but not Asset/Liability/Equity (e.g. Revenue,
                    // Expense) — correctly out of scope for a balance sheet.
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
                    default:
                        equityLines.Add(new BalanceSheetLineDto(account.Code, account.Name, -b.Net));
                        break;
                }
            }

            var orderedAssets = assetLines.OrderBy(l => l.AccountCode, StringComparer.Ordinal).ToList();
            var orderedLiabilities = liabilityLines.OrderBy(l => l.AccountCode, StringComparer.Ordinal).ToList();
            var orderedEquity = equityLines.OrderBy(l => l.AccountCode, StringComparer.Ordinal).ToList();

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
