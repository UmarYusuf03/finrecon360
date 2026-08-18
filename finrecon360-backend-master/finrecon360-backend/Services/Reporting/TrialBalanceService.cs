using finrecon360_backend.Data;
using finrecon360_backend.Dtos.Reporting;
using Microsoft.EntityFrameworkCore;

namespace finrecon360_backend.Services.Reporting
{
    public interface ITrialBalanceService
    {
        Task<TrialBalanceResponse> GetAsync(TenantDbContext db, DateTime asOfUtc, CancellationToken ct = default);
    }

    /// <summary>
    /// Sums debits and credits per account as of a given date. Sign convention (established by
    /// JournalPostingExecutorWorker/ReconciliationController, the only writers of JournalEntry):
    /// JournalEntry.Amount is a single signed column, positive = debit, negative = credit — not
    /// separate Debit/Credit columns. Every JournalVoucher is required to sum to zero before it's
    /// allowed to post, so the trial balance across ALL accounts (including Unclassified) is
    /// mathematically guaranteed to balance; IsBalanced is a data-integrity check on that
    /// invariant, not a display convenience.
    /// </summary>
    public class TrialBalanceService : ITrialBalanceService
    {
        public async Task<TrialBalanceResponse> GetAsync(
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

            var lines = balances.Select(b =>
            {
                var (code, name, accountType) = GeneralLedgerService.ResolveAccount(b.ChartOfAccountId, accountsById);
                var debit = b.Net >= 0 ? b.Net : 0m;
                var credit = b.Net < 0 ? -b.Net : 0m;
                return new TrialBalanceLineDto(b.ChartOfAccountId, code, name, accountType, debit, credit);
            })
            .OrderBy(l => l.AccountCode == UnclassifiedAccount.Code ? 1 : 0)
            .ThenBy(l => l.AccountCode, StringComparer.Ordinal)
            .ToList();

            var totalDebit = lines.Sum(l => l.Debit);
            var totalCredit = lines.Sum(l => l.Credit);

            return new TrialBalanceResponse(asOfUtc, lines, totalDebit, totalCredit, totalDebit == totalCredit);
        }
    }
}
