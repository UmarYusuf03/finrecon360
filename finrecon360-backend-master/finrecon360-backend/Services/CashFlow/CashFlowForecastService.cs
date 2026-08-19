using finrecon360_backend.Data;
using finrecon360_backend.Dtos.CashFlow;
using finrecon360_backend.Models;
using Microsoft.EntityFrameworkCore;

namespace finrecon360_backend.Services.CashFlow
{
    public interface ICashFlowForecastService
    {
        Task<CashFlowForecastResponse> GetForecastAsync(
            TenantDbContext db,
            Guid? bankAccountId,
            int horizonDays,
            CancellationToken ct = default);
    }

    /// <summary>
    /// WHY: A lightweight, explainable forecast rather than an ML model — this domain doesn't have
    /// the data volume to train one, and an admin deciding whether they can make payroll needs to
    /// be able to see *why* the number is what it is, not trust a black box. The method is:
    /// an exponentially-weighted moving average of confirmed (JournalReady) daily net flow gives
    /// the steady-state trend, and known pending card cash-outs (NeedsBankMatch) are layered on
    /// top at their expected settlement date, estimated from how long this tenant's past
    /// NeedsBankMatch -> JournalReady transitions actually took.
    /// </summary>
    public class CashFlowForecastService : ICashFlowForecastService
    {
        private const int LookbackDays = 90;
        private const int EwmaSpanDays = 14;
        private const int DefaultSettlementLagDays = 3;
        private const int MinSettlementLagSamples = 3;
        private const int MaxSettlementLagDays = 30;

        public async Task<CashFlowForecastResponse> GetForecastAsync(
            TenantDbContext db,
            Guid? bankAccountId,
            int horizonDays,
            CancellationToken ct = default)
        {
            var today = DateTime.UtcNow.Date;
            var lookbackStart = today.AddDays(-LookbackDays);

            var accountIds = bankAccountId.HasValue
                ? new List<Guid> { bankAccountId.Value }
                : await db.BankAccounts.AsNoTracking()
                    .Where(a => a.IsActive)
                    .Select(a => a.BankAccountId)
                    .ToListAsync(ct);

            var dailyNet = BuildEmptyDailySeries(lookbackStart, today);

            if (accountIds.Count > 0)
            {
                var confirmedTransactions = await db.Transactions.AsNoTracking()
                    .Where(t => t.BankAccountId != null
                        && accountIds.Contains(t.BankAccountId.Value)
                        && t.TransactionState == TransactionState.JournalReady
                        && t.TransactionDate >= lookbackStart
                        && t.TransactionDate <= today)
                    .Select(t => new { t.TransactionDate, t.Amount, t.TransactionType })
                    .ToListAsync(ct);

                foreach (var t in confirmedTransactions)
                {
                    var day = t.TransactionDate.Date;
                    var signedAmount = t.TransactionType == TransactionType.CashIn ? t.Amount : -t.Amount;
                    dailyNet[day] = dailyNet.GetValueOrDefault(day) + signedAmount;
                }
            }

            var orderedDailyValues = dailyNet.OrderBy(kv => kv.Key).Select(kv => kv.Value).ToList();
            var dailyAverageNetFlow = ComputeEwma(orderedDailyValues, EwmaSpanDays);
            var settlementLagDays = await EstimateSettlementLagDaysAsync(db, accountIds, ct);
            var pendingByDate = await BuildPendingByDateAsync(db, accountIds, settlementLagDays, today, ct);

            var forecast = new List<CashFlowForecastDayDto>();
            var cumulative = 0m;
            for (var i = 1; i <= horizonDays; i++)
            {
                var day = today.AddDays(i);
                var known = pendingByDate.GetValueOrDefault(day);
                var projected = dailyAverageNetFlow + known;
                cumulative += projected;
                forecast.Add(new CashFlowForecastDayDto(day, Math.Round(projected, 2), Math.Round(cumulative, 2), Math.Round(known, 2)));
            }

            var history = dailyNet.OrderBy(kv => kv.Key)
                .Select(kv => new CashFlowHistoryDayDto(kv.Key, Math.Round(kv.Value, 2)))
                .ToList();

            var bankAccountName = bankAccountId.HasValue
                ? await db.BankAccounts.AsNoTracking()
                    .Where(a => a.BankAccountId == bankAccountId.Value)
                    .Select(a => a.BankName + " · " + a.AccountNumber)
                    .FirstOrDefaultAsync(ct)
                : "All active bank accounts";

            return new CashFlowForecastResponse(
                bankAccountId,
                bankAccountName ?? "Bank account",
                DateTime.UtcNow,
                LookbackDays,
                Math.Round(dailyAverageNetFlow, 2),
                settlementLagDays,
                history,
                forecast);
        }

        private static Dictionary<DateTime, decimal> BuildEmptyDailySeries(DateTime start, DateTime end)
        {
            var series = new Dictionary<DateTime, decimal>();
            for (var day = start; day <= end; day = day.AddDays(1))
            {
                series[day] = 0m;
            }

            return series;
        }

        /// <summary>
        /// Exponentially-weighted moving average: recent days count more than old ones, which is
        /// what you want for "what does this tenant's cash flow look like right now" rather than a
        /// flat average that lets a quiet month three months ago cancel out a busy last two weeks.
        /// </summary>
        private static decimal ComputeEwma(IReadOnlyList<decimal> series, int span)
        {
            if (series.Count == 0)
            {
                return 0m;
            }

            var alpha = 2m / (span + 1);
            var ewma = series[0];
            for (var i = 1; i < series.Count; i++)
            {
                ewma = alpha * series[i] + (1 - alpha) * ewma;
            }

            return ewma;
        }

        private static async Task<int> EstimateSettlementLagDaysAsync(TenantDbContext db, List<Guid> accountIds, CancellationToken ct)
        {
            if (accountIds.Count == 0)
            {
                return DefaultSettlementLagDays;
            }

            var settledTransactionIds = await db.Transactions.AsNoTracking()
                .Where(t => t.BankAccountId != null
                    && accountIds.Contains(t.BankAccountId.Value)
                    && t.TransactionState == TransactionState.JournalReady)
                .Select(t => t.TransactionId)
                .ToListAsync(ct);

            if (settledTransactionIds.Count == 0)
            {
                return DefaultSettlementLagDays;
            }

            var relevantHistory = await db.TransactionStateHistories.AsNoTracking()
                .Where(h => settledTransactionIds.Contains(h.TransactionId)
                    && (h.ToState == TransactionState.NeedsBankMatch || h.ToState == TransactionState.JournalReady))
                .Select(h => new { h.TransactionId, h.ToState, h.ChangedAt })
                .ToListAsync(ct);

            var lagsInDays = new List<double>();
            foreach (var group in relevantHistory.GroupBy(h => h.TransactionId))
            {
                var enteredNeedsMatch = group
                    .Where(h => h.ToState == TransactionState.NeedsBankMatch)
                    .Select(h => (DateTime?)h.ChangedAt)
                    .Max();
                var enteredJournalReady = group
                    .Where(h => h.ToState == TransactionState.JournalReady)
                    .Select(h => (DateTime?)h.ChangedAt)
                    .Max();

                if (enteredNeedsMatch.HasValue && enteredJournalReady.HasValue && enteredJournalReady > enteredNeedsMatch)
                {
                    lagsInDays.Add((enteredJournalReady.Value - enteredNeedsMatch.Value).TotalDays);
                }
            }

            if (lagsInDays.Count < MinSettlementLagSamples)
            {
                return DefaultSettlementLagDays;
            }

            var averageLag = (int)Math.Round(lagsInDays.Average());
            return Math.Clamp(averageLag, 0, MaxSettlementLagDays);
        }

        private static async Task<Dictionary<DateTime, decimal>> BuildPendingByDateAsync(
            TenantDbContext db,
            List<Guid> accountIds,
            int settlementLagDays,
            DateTime today,
            CancellationToken ct)
        {
            var pendingByDate = new Dictionary<DateTime, decimal>();
            if (accountIds.Count == 0)
            {
                return pendingByDate;
            }

            var pendingTransactions = await db.Transactions.AsNoTracking()
                .Where(t => t.BankAccountId != null
                    && accountIds.Contains(t.BankAccountId.Value)
                    && t.TransactionState == TransactionState.NeedsBankMatch)
                .Select(t => new { t.TransactionId, t.Amount, t.TransactionType, t.ApprovedAt, t.TransactionDate })
                .ToListAsync(ct);

            if (pendingTransactions.Count == 0)
            {
                return pendingByDate;
            }

            var pendingIds = pendingTransactions.Select(p => p.TransactionId).ToList();
            var enteredNeedsMatchAt = await db.TransactionStateHistories.AsNoTracking()
                .Where(h => pendingIds.Contains(h.TransactionId) && h.ToState == TransactionState.NeedsBankMatch)
                .GroupBy(h => h.TransactionId)
                .Select(g => new { TransactionId = g.Key, ChangedAt = g.Max(h => h.ChangedAt) })
                .ToDictionaryAsync(x => x.TransactionId, x => x.ChangedAt, ct);

            foreach (var t in pendingTransactions)
            {
                var anchor = enteredNeedsMatchAt.TryGetValue(t.TransactionId, out var changedAt)
                    ? changedAt
                    : (t.ApprovedAt ?? t.TransactionDate);

                var expectedDate = anchor.Date.AddDays(settlementLagDays);
                if (expectedDate < today)
                {
                    // Already overdue relative to the historical lag estimate — place it on the
                    // earliest forecast day instead of silently dropping it from the projection.
                    expectedDate = today.AddDays(1);
                }

                var signedAmount = t.TransactionType == TransactionType.CashIn ? t.Amount : -t.Amount;
                pendingByDate[expectedDate] = pendingByDate.GetValueOrDefault(expectedDate) + signedAmount;
            }

            return pendingByDate;
        }
    }
}
