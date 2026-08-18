using finrecon360_backend.Data;
using finrecon360_backend.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace finrecon360_backend.Services.Workers
{
    /// <summary>
    /// WHY: Computes one day's worth of reconciliation trend data (per MatchLevel) and upserts it
    /// into ReconciliationDailySnapshot. Split from ReconciliationSnapshotHostedService the same
    /// way JournalPostingExecutorWorker is split from JournalPostingHostedService — the hosted
    /// service owns the tenant loop and scheduling, this owns the actual computation and is
    /// directly unit-testable against an in-memory TenantDbContext.
    ///
    /// Idempotent: re-running for a date/tenant that already has rows updates them in place
    /// rather than duplicating, so a restart mid-cycle or a manual backfill re-run is safe.
    /// </summary>
    public interface IReconciliationSnapshotWorker
    {
        Task<ReconciliationSnapshotResult> ExecuteAsync(
            Guid tenantId,
            TenantDbContext tenantDb,
            DateTime snapshotDate,
            CancellationToken cancellationToken = default);
    }

    public record ReconciliationSnapshotResult(DateTime SnapshotDate, int RowsUpserted, string Summary);

    public class ReconciliationSnapshotWorker : IReconciliationSnapshotWorker
    {
        private readonly ILogger<ReconciliationSnapshotWorker> _logger;

        public ReconciliationSnapshotWorker(ILogger<ReconciliationSnapshotWorker> logger)
        {
            _logger = logger;
        }

        public async Task<ReconciliationSnapshotResult> ExecuteAsync(
            Guid tenantId,
            TenantDbContext tenantDb,
            DateTime snapshotDate,
            CancellationToken cancellationToken = default)
        {
            var day = snapshotDate.Date;
            var windowStart = day;
            var windowEnd = day.AddDays(1);

            var rows = new Dictionary<string, SnapshotAccumulator>();

            // Match groups created on this day, per level.
            var createdCounts = await tenantDb.ReconciliationMatchGroups
                .AsNoTracking()
                .Where(g => g.CreatedAt >= windowStart && g.CreatedAt < windowEnd)
                .GroupBy(g => g.MatchLevel)
                .Select(g => new { Level = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken);

            foreach (var row in createdCounts)
            {
                Accumulator(rows, row.Level).MatchedCount = row.Count;
            }

            // Match groups confirmed on this day, per level, plus average dwell time
            // (CreatedAt -> ConfirmedAt) for the ones confirmed today — this is the closest
            // available proxy for "time to match" given the fields the model actually has.
            var confirmedGroups = await tenantDb.ReconciliationMatchGroups
                .AsNoTracking()
                .Where(g => g.ConfirmedAt != null && g.ConfirmedAt >= windowStart && g.ConfirmedAt < windowEnd)
                .Select(g => new { g.MatchLevel, g.CreatedAt, ConfirmedAt = g.ConfirmedAt!.Value })
                .ToListAsync(cancellationToken);

            foreach (var group in confirmedGroups.GroupBy(g => g.MatchLevel))
            {
                var accumulator = Accumulator(rows, group.Key);
                accumulator.ConfirmedCount = group.Count();
                accumulator.AverageTimeToMatchHours = (decimal)group.Average(g => (g.ConfirmedAt - g.CreatedAt).TotalHours);
            }

            // Variance events logged on this day, per level.
            var exceptionCounts = await tenantDb.ReconciliationEvents
                .AsNoTracking()
                .Where(e => e.EventType == "Variance" && e.CreatedAt >= windowStart && e.CreatedAt < windowEnd)
                .GroupBy(e => e.MatchLevel)
                .Select(g => new { Level = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken);

            foreach (var row in exceptionCounts)
            {
                Accumulator(rows, row.Level).ExceptionCount = row.Count;
            }

            // Unmatched backlog composition by original transaction date — scoped to Level4
            // (Expense Match, BANK vs approved card cashout), mirroring
            // ReconciliationMatchConfirmationService.GetSummaryAsync's current scope. "As of now,
            // how many of the items dated this day are still sitting unmatched."
            var unmatchedForDay = await (
                from record in tenantDb.ImportedNormalizedRecords.AsNoTracking()
                join batch in tenantDb.ImportBatches.AsNoTracking()
                    on record.ImportBatchId equals batch.ImportBatchId
                where batch.SourceType == "BANK"
                    && batch.Status == "COMMITTED"
                    && record.MatchStatus == "PENDING"
                    && record.TransactionDate >= windowStart
                    && record.TransactionDate < windowEnd
                select record.ImportedNormalizedRecordId
            ).CountAsync(cancellationToken);

            if (unmatchedForDay > 0)
            {
                Accumulator(rows, "Level4").UnmatchedCount = unmatchedForDay;
            }

            if (rows.Count == 0)
            {
                _logger.LogDebug("No reconciliation activity for tenant {TenantId} on {Date}; nothing to snapshot", tenantId, day.ToString("yyyy-MM-dd"));
                return new ReconciliationSnapshotResult(day, 0, "No activity to snapshot");
            }

            var existing = await tenantDb.ReconciliationDailySnapshots
                .Where(s => s.SnapshotDate == day)
                .ToDictionaryAsync(s => s.MatchLevel, cancellationToken);

            foreach (var (level, accumulator) in rows)
            {
                if (existing.TryGetValue(level, out var row))
                {
                    row.MatchedCount = accumulator.MatchedCount;
                    row.ConfirmedCount = accumulator.ConfirmedCount;
                    row.ExceptionCount = accumulator.ExceptionCount;
                    row.UnmatchedCount = accumulator.UnmatchedCount;
                    row.AverageTimeToMatchHours = accumulator.AverageTimeToMatchHours;
                }
                else
                {
                    tenantDb.ReconciliationDailySnapshots.Add(new ReconciliationDailySnapshot
                    {
                        ReconciliationDailySnapshotId = Guid.NewGuid(),
                        SnapshotDate = day,
                        MatchLevel = level,
                        MatchedCount = accumulator.MatchedCount,
                        ConfirmedCount = accumulator.ConfirmedCount,
                        ExceptionCount = accumulator.ExceptionCount,
                        UnmatchedCount = accumulator.UnmatchedCount,
                        AverageTimeToMatchHours = accumulator.AverageTimeToMatchHours,
                        CreatedAt = DateTime.UtcNow,
                    });
                }
            }

            await tenantDb.SaveChangesAsync(cancellationToken);

            var summary = $"Snapshotted {rows.Count} level(s) for {day:yyyy-MM-dd}";
            _logger.LogInformation("Reconciliation snapshot for tenant {TenantId}: {Summary}", tenantId, summary);
            return new ReconciliationSnapshotResult(day, rows.Count, summary);
        }

        private static SnapshotAccumulator Accumulator(Dictionary<string, SnapshotAccumulator> rows, string level)
        {
            if (!rows.TryGetValue(level, out var accumulator))
            {
                accumulator = new SnapshotAccumulator();
                rows[level] = accumulator;
            }

            return accumulator;
        }

        private class SnapshotAccumulator
        {
            public int MatchedCount { get; set; }
            public int ConfirmedCount { get; set; }
            public int ExceptionCount { get; set; }
            public int UnmatchedCount { get; set; }
            public decimal? AverageTimeToMatchHours { get; set; }
        }
    }
}
