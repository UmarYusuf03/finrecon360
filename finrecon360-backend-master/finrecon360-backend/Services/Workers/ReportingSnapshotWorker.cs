using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using finrecon360_backend.Data;
using finrecon360_backend.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace finrecon360_backend.Services.Workers
{
    public interface IReportingSnapshotWorker
    {
        Task ExecuteAsync(Guid tenantId, TenantDbContext tenantDb, CancellationToken cancellationToken = default);
    }

    public class ReportingSnapshotWorker : IReportingSnapshotWorker
    {
        private readonly ILogger<ReportingSnapshotWorker> _logger;

        public ReportingSnapshotWorker(ILogger<ReportingSnapshotWorker> logger)
        {
            _logger = logger;
        }

        public async Task ExecuteAsync(Guid tenantId, TenantDbContext tenantDb, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Generating Reporting Snapshot for tenant {TenantId}", tenantId);

            var snapshotDate = DateTime.UtcNow.Date;

            // 1. Calculate Unmatched Card Cashouts (Crucial KPI)
            var totalUnmatchedCardCashouts = await tenantDb.Transactions
                .Where(x => x.PaymentMethod == PaymentMethod.Card 
                         && x.TransactionType == TransactionType.CashOut 
                         && x.TransactionState == TransactionState.NeedsBankMatch)
                .CountAsync(cancellationToken);

            // 2. Calculate Pending Exceptions
            var pendingExceptions = await tenantDb.ReconciliationEvents
                .Where(e => e.Status == "Pending" || e.Status == "RequiresReview")
                .CountAsync(cancellationToken);

            // 3. Calculate Total Journal Ready
            var totalJournalReady = await tenantDb.Transactions
                .Where(t => t.TransactionState == TransactionState.JournalReady)
                .CountAsync(cancellationToken);

            // 4. Calculate Total Confirmed Match Groups
            var totalMatchGroupsConfirmed = await tenantDb.ReconciliationMatchGroups
                .Where(g => g.IsConfirmed)
                .CountAsync(cancellationToken);

            // 5. Calculate Total Fee Adjustments
            var totalFeeAdjustments = await tenantDb.JournalEntries
                .Where(j => j.EntryType == "FeeAdjustment")
                .SumAsync(j => j.Amount, cancellationToken);

            // 6. Calculate Reconciliation Completion Percentage (Last 30 Days)
            var cutoff = DateTime.UtcNow.AddDays(-30);
            var totalRecords = await tenantDb.ImportedNormalizedRecords
                .Where(r => r.CreatedAt >= cutoff)
                .CountAsync(cancellationToken);

            var matchedRecords = await tenantDb.ImportedNormalizedRecords
                .Where(r => r.CreatedAt >= cutoff && r.MatchStatus == "MATCHED")
                .CountAsync(cancellationToken);

            decimal completionPercentage = totalRecords == 0 ? 100m : Math.Round((decimal)matchedRecords * 100 / totalRecords, 2);

            // 7. Upsert Snapshot
            var existingSnapshot = await tenantDb.ReportSnapshots
                .FirstOrDefaultAsync(s => s.SnapshotDate == snapshotDate, cancellationToken);

            if (existingSnapshot != null)
            {
                existingSnapshot.TotalUnmatchedCardCashouts = totalUnmatchedCardCashouts;
                existingSnapshot.PendingExceptions = pendingExceptions;
                existingSnapshot.TotalJournalReady = totalJournalReady;
                existingSnapshot.TotalMatchGroupsConfirmed = totalMatchGroupsConfirmed;
                existingSnapshot.TotalFeeAdjustments = totalFeeAdjustments;
                existingSnapshot.ReconciliationCompletionPercentage = completionPercentage;
                
                tenantDb.ReportSnapshots.Update(existingSnapshot);
            }
            else
            {
                var newSnapshot = new ReportSnapshot
                {
                    ReportSnapshotId = Guid.NewGuid(),
                    SnapshotDate = snapshotDate,
                    TotalUnmatchedCardCashouts = totalUnmatchedCardCashouts,
                    PendingExceptions = pendingExceptions,
                    TotalJournalReady = totalJournalReady,
                    TotalMatchGroupsConfirmed = totalMatchGroupsConfirmed,
                    TotalFeeAdjustments = totalFeeAdjustments,
                    ReconciliationCompletionPercentage = completionPercentage,
                    CreatedAt = DateTime.UtcNow
                };

                tenantDb.ReportSnapshots.Add(newSnapshot);
            }

            await tenantDb.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Successfully generated snapshot for {SnapshotDate} (Tenant: {TenantId})", snapshotDate, tenantId);
        }
    }
}
