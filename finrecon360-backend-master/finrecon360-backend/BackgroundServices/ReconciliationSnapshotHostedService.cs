using finrecon360_backend.Data;
using finrecon360_backend.Models;
using finrecon360_backend.Services;
using finrecon360_backend.Services.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace finrecon360_backend.BackgroundServices
{
    /// <summary>
    /// WHY: Runs ReconciliationSnapshotWorker and TenantDailySnapshotWorker for every active
    /// tenant once daily, rolling up yesterday's (UTC) activity into ReconciliationDailySnapshot
    /// and TenantDailySnapshot in the same pass — one tenant DB connection per tenant covers both
    /// (Phase 4 extends Phase 3's hosted service rather than adding a second one, per the
    /// reporting plan's "or extend Phase 3's" note). Staggered well clear of
    /// ReconciliationCycleHostedService (5 min) and JournalPostingHostedService (5 min, +30s
    /// offset) — this only needs to run once a day and reads their output (match groups, events,
    /// journal entries) rather than competing with them for tenant DB access on every cycle.
    ///
    /// Runs once per RunInterval regardless of wall-clock time (no attempt to align to
    /// midnight) — both workers are idempotent per (tenant, date), so an extra run just
    /// re-upserts the same day's numbers with no ill effect.
    /// </summary>
    public class ReconciliationSnapshotHostedService : BackgroundService
    {
        private static readonly TimeSpan RunInterval = TimeSpan.FromHours(24);
        private static readonly Dictionary<Guid, bool> RunningTenants = new();

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ReconciliationSnapshotHostedService> _logger;

        public ReconciliationSnapshotHostedService(
            IServiceScopeFactory scopeFactory,
            ILogger<ReconciliationSnapshotHostedService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Reconciliation snapshot hosted service started");

            // Starts well after the reconciliation cycle and journal posting services so the
            // first snapshot run isn't racing their startup delays for tenant DB connections.
            await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    RunCycle(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled exception in reconciliation snapshot cycle");
                }

                await Task.Delay(RunInterval, stoppingToken);
            }

            _logger.LogInformation("Reconciliation snapshot hosted service stopped");
        }

        private void RunCycle(CancellationToken stoppingToken)
        {
            var snapshotDate = DateTime.UtcNow.Date.AddDays(-1);

            using var scope = _scopeFactory.CreateScope();
            var appDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var activeTenants = appDb.Tenants
                .AsNoTracking()
                .Where(t => t.Status == TenantStatus.Active)
                .Select(t => t.TenantId)
                .ToList();

            foreach (var tenantId in activeTenants)
            {
                if (RunningTenants.TryGetValue(tenantId, out var isRunning) && isRunning)
                {
                    _logger.LogDebug("Skipping tenant {TenantId}; snapshot cycle already in progress", tenantId);
                    continue;
                }

                _ = ProcessTenantAsync(tenantId, snapshotDate, stoppingToken);
            }
        }

        private async Task ProcessTenantAsync(Guid tenantId, DateTime snapshotDate, CancellationToken cancellationToken)
        {
            lock (RunningTenants)
            {
                RunningTenants[tenantId] = true;
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var tenantDbContextFactory = scope.ServiceProvider.GetRequiredService<ITenantDbContextFactory>();
                await using var tenantDb = await tenantDbContextFactory.CreateAsync(tenantId, cancellationToken);

                try
                {
                    var worker = scope.ServiceProvider.GetRequiredService<IReconciliationSnapshotWorker>();
                    var result = await worker.ExecuteAsync(tenantId, tenantDb, snapshotDate, cancellationToken);
                    _logger.LogInformation("Reconciliation snapshot completed for tenant {TenantId}: {Summary}",
                        tenantId, result.Summary);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Reconciliation snapshot failed for tenant {TenantId}", tenantId);
                }

                try
                {
                    var tenantWorker = scope.ServiceProvider.GetRequiredService<ITenantDailySnapshotWorker>();
                    var result = await tenantWorker.ExecuteAsync(tenantId, tenantDb, snapshotDate, cancellationToken);
                    _logger.LogInformation("Tenant daily snapshot completed for tenant {TenantId}: {Summary}",
                        tenantId, result.Summary);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Tenant daily snapshot failed for tenant {TenantId}", tenantId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Daily snapshot cycle failed for tenant {TenantId}", tenantId);
            }
            finally
            {
                lock (RunningTenants)
                {
                    RunningTenants[tenantId] = false;
                }
            }
        }
    }
}
