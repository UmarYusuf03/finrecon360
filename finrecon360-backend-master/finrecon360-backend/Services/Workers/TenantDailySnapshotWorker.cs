using finrecon360_backend.Data;
using finrecon360_backend.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace finrecon360_backend.Services.Workers
{
    /// <summary>
    /// WHY: Tenant-wide counterpart to ReconciliationSnapshotWorker — computes one day's approval
    /// backlog, journal posting summary, and bank reconciliation progress into
    /// TenantDailySnapshot. Idempotent per (tenant, date), same as its reconciliation sibling.
    /// </summary>
    public interface ITenantDailySnapshotWorker
    {
        Task<TenantDailySnapshotResult> ExecuteAsync(
            Guid tenantId,
            TenantDbContext tenantDb,
            DateTime snapshotDate,
            CancellationToken cancellationToken = default);
    }

    public record TenantDailySnapshotResult(DateTime SnapshotDate, string Summary);

    public class TenantDailySnapshotWorker : ITenantDailySnapshotWorker
    {
        private readonly ILogger<TenantDailySnapshotWorker> _logger;

        public TenantDailySnapshotWorker(ILogger<TenantDailySnapshotWorker> logger)
        {
            _logger = logger;
        }

        public async Task<TenantDailySnapshotResult> ExecuteAsync(
            Guid tenantId,
            TenantDbContext tenantDb,
            DateTime snapshotDate,
            CancellationToken cancellationToken = default)
        {
            var day = snapshotDate.Date;
            var windowStart = day;
            var windowEnd = day.AddDays(1);
            var now = DateTime.UtcNow;

            // Approval backlog: how many of today's transactions are still Pending right now,
            // and how old is the single oldest Pending transaction tenant-wide right now.
            var pendingApprovalCount = await tenantDb.Transactions
                .AsNoTracking()
                .CountAsync(t => t.TransactionState == TransactionState.Pending
                    && t.CreatedAt >= windowStart && t.CreatedAt < windowEnd, cancellationToken);

            var oldestPendingCreatedAt = await tenantDb.Transactions
                .AsNoTracking()
                .Where(t => t.TransactionState == TransactionState.Pending)
                .OrderBy(t => t.CreatedAt)
                .Select(t => (DateTime?)t.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            var oldestPendingApprovalAgeHours = oldestPendingCreatedAt.HasValue
                ? (decimal)(now - oldestPendingCreatedAt.Value).TotalHours
                : (decimal?)null;

            // Journal posting summary for the day.
            var journalEntries = await tenantDb.JournalEntries
                .AsNoTracking()
                .Where(e => e.PostedAt >= windowStart && e.PostedAt < windowEnd)
                .Select(e => e.Amount)
                .ToListAsync(cancellationToken);

            var journalEntriesPostedCount = journalEntries.Count;
            var journalDebitAmountPosted = journalEntries.Where(a => a > 0).Sum();

            // Bank reconciliation progress: committed BANK records dated this day, and how many
            // are matched as of now.
            var bankRecordsTotalCount = await (
                from record in tenantDb.ImportedNormalizedRecords.AsNoTracking()
                join batch in tenantDb.ImportBatches.AsNoTracking()
                    on record.ImportBatchId equals batch.ImportBatchId
                where batch.SourceType == "BANK"
                    && batch.Status == "COMMITTED"
                    && record.TransactionDate >= windowStart
                    && record.TransactionDate < windowEnd
                select record.ImportedNormalizedRecordId
            ).CountAsync(cancellationToken);

            var bankRecordsMatchedCount = await (
                from record in tenantDb.ImportedNormalizedRecords.AsNoTracking()
                join batch in tenantDb.ImportBatches.AsNoTracking()
                    on record.ImportBatchId equals batch.ImportBatchId
                where batch.SourceType == "BANK"
                    && batch.Status == "COMMITTED"
                    && record.TransactionDate >= windowStart
                    && record.TransactionDate < windowEnd
                    && record.MatchStatus != "PENDING"
                select record.ImportedNormalizedRecordId
            ).CountAsync(cancellationToken);

            var existing = await tenantDb.TenantDailySnapshots
                .FirstOrDefaultAsync(s => s.SnapshotDate == day, cancellationToken);

            if (existing != null)
            {
                existing.PendingApprovalCount = pendingApprovalCount;
                existing.OldestPendingApprovalAgeHours = oldestPendingApprovalAgeHours;
                existing.JournalEntriesPostedCount = journalEntriesPostedCount;
                existing.JournalDebitAmountPosted = journalDebitAmountPosted;
                existing.BankRecordsTotalCount = bankRecordsTotalCount;
                existing.BankRecordsMatchedCount = bankRecordsMatchedCount;
            }
            else
            {
                tenantDb.TenantDailySnapshots.Add(new TenantDailySnapshot
                {
                    TenantDailySnapshotId = Guid.NewGuid(),
                    SnapshotDate = day,
                    PendingApprovalCount = pendingApprovalCount,
                    OldestPendingApprovalAgeHours = oldestPendingApprovalAgeHours,
                    JournalEntriesPostedCount = journalEntriesPostedCount,
                    JournalDebitAmountPosted = journalDebitAmountPosted,
                    BankRecordsTotalCount = bankRecordsTotalCount,
                    BankRecordsMatchedCount = bankRecordsMatchedCount,
                    CreatedAt = now,
                });
            }

            await tenantDb.SaveChangesAsync(cancellationToken);

            var summary = $"pendingApproval={pendingApprovalCount}, journalPosted={journalEntriesPostedCount}, " +
                $"bankMatched={bankRecordsMatchedCount}/{bankRecordsTotalCount}";
            _logger.LogInformation("Tenant daily snapshot for tenant {TenantId} on {Date}: {Summary}",
                tenantId, day.ToString("yyyy-MM-dd"), summary);

            return new TenantDailySnapshotResult(day, summary);
        }
    }
}
