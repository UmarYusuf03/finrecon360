using finrecon360_backend.Data;
using finrecon360_backend.Dtos.Reporting;
using Microsoft.EntityFrameworkCore;

namespace finrecon360_backend.Services.Reporting
{
    public interface IGeneralLedgerService
    {
        Task<GeneralLedgerResponse> GetAsync(TenantDbContext db, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);
    }

    /// <summary>
    /// Per-account listing of JournalEntry rows in [fromUtc, toUtc] with a running balance,
    /// seeded from an opening balance computed over everything posted before fromUtc. The
    /// simplest report and the one the others (Trial Balance, P&amp;L, Balance Sheet) build on —
    /// they all group the same JournalEntry.Amount data differently.
    /// </summary>
    public class GeneralLedgerService : IGeneralLedgerService
    {
        public async Task<GeneralLedgerResponse> GetAsync(
            TenantDbContext db,
            DateTime fromUtc,
            DateTime toUtc,
            CancellationToken ct = default)
        {
            // Dictionary<Guid?, T> throws ArgumentNullException on a null key at runtime (the
            // `notnull` constraint warning is not just cosmetic) — and a null ChartOfAccountId is
            // exactly the Unclassified bucket this report needs to support, so the classified and
            // unclassified opening balances are tracked separately instead of in one dictionary.
            var openingRows = await db.JournalEntries
                .AsNoTracking()
                .Where(e => e.PostedAt < fromUtc)
                .GroupBy(e => e.ChartOfAccountId)
                .Select(g => new { ChartOfAccountId = g.Key, Total = g.Sum(e => e.Amount) })
                .ToListAsync(ct);

            var openingBalances = openingRows
                .Where(r => r.ChartOfAccountId.HasValue)
                .ToDictionary(r => r.ChartOfAccountId!.Value, r => r.Total);
            var openingUnclassifiedBalance = openingRows
                .Where(r => !r.ChartOfAccountId.HasValue)
                .Sum(r => r.Total);

            var rangeEntries = await db.JournalEntries
                .AsNoTracking()
                .Where(e => e.PostedAt >= fromUtc && e.PostedAt <= toUtc)
                .OrderBy(e => e.PostedAt)
                .ToListAsync(ct);

            var accountsById = await db.ChartOfAccounts
                .AsNoTracking()
                .ToDictionaryAsync(a => a.ChartOfAccountId, ct);

            var accounts = new List<GeneralLedgerAccountDto>();

            foreach (var group in rangeEntries.GroupBy(e => e.ChartOfAccountId))
            {
                var opening = group.Key.HasValue
                    ? openingBalances.GetValueOrDefault(group.Key.Value, 0m)
                    : openingUnclassifiedBalance;
                var running = opening;
                var entryDtos = new List<GeneralLedgerEntryDto>();

                foreach (var entry in group.OrderBy(e => e.PostedAt))
                {
                    running += entry.Amount;
                    entryDtos.Add(new GeneralLedgerEntryDto(
                        entry.PostedAt,
                        entry.JournalEntryId,
                        entry.JournalVoucherId,
                        entry.EntryType,
                        entry.Notes,
                        entry.Amount,
                        running));
                }

                var (code, name, accountType) = ResolveAccount(group.Key, accountsById);
                accounts.Add(new GeneralLedgerAccountDto(group.Key, code, name, accountType, opening, running, entryDtos));
            }

            var ordered = accounts
                .OrderBy(a => a.AccountCode == UnclassifiedAccount.Code ? 1 : 0)
                .ThenBy(a => a.AccountCode, StringComparer.Ordinal)
                .ToList();

            return new GeneralLedgerResponse(fromUtc, toUtc, ordered);
        }

        internal static (string Code, string Name, string? AccountType) ResolveAccount(
            Guid? chartOfAccountId,
            IReadOnlyDictionary<Guid, Models.ChartOfAccount> accountsById)
        {
            if (chartOfAccountId.HasValue && accountsById.TryGetValue(chartOfAccountId.Value, out var account))
            {
                return (account.Code, account.Name, account.AccountType.ToString());
            }

            return (UnclassifiedAccount.Code, UnclassifiedAccount.Name, null);
        }
    }
}
