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
    /// WHY: Runs the reconciliation chain for every active tenant on a timer. Replaces the
    /// single-worker BankReconciliationHostedService — that only ever ran Level4
    /// (BankStatementReconciliationWorker), leaving OperationalMatchWorker,
    /// PosErpSyncAuditWorker, ErpGatewaySalesMatchWorker, and SettlementMatchWorker
    /// registered but never invoked by anything.
    ///
    /// Level5 (CollectionMatchWorker, staff-entered card-in Transaction ↔ Bank) was removed —
    /// every card-in collection arrives via a POS batch file, which is Level7's territory, so
    /// Level5 never had candidates to act on. Levels run 1→2→3→4→6→7 (no gap in numbering
    /// semantics — 5 is simply retired, not renumbered, since historical Level5 match-group
    /// rows may still exist in tenant databases).
    ///
    /// Behavior:
    /// - Runs on an interval (default: every 1 minute — tightened 2026-08-25 from the original
    ///   5 minutes so a transient SQL Server outage costs at most ~1 minute of matching lag once
    ///   the API process is back up; the outer loop already retries on this same interval after
    ///   a failed cycle, so this doesn't change failure handling, only how quickly it recovers)
    /// - For each active tenant, runs the matching workers in Level order using one
    ///   TenantDbContext per cycle
    /// - Each worker runs in its own try/catch so one worker's failure doesn't block the rest
    /// - Safely handles concurrent execution; skips a tenant if its previous cycle hasn't finished
    /// - JournalPostingHostedService runs separately (staggered 30s later) and consumes the
    ///   match groups this cycle produces
    /// </summary>
    public class ReconciliationCycleHostedService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ReconciliationCycleHostedService> _logger;

        private static readonly TimeSpan RunInterval = TimeSpan.FromMinutes(1);
        private static readonly Dictionary<Guid, bool> RunningTenants = new();

        public ReconciliationCycleHostedService(
            IServiceScopeFactory scopeFactory,
            ILogger<ReconciliationCycleHostedService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Reconciliation cycle hosted service started");

            // Delay startup to allow application to fully initialize
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogDebug("Starting reconciliation cycle");

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
                            _logger.LogDebug("Skipping tenant {TenantId}; reconciliation cycle already in progress", tenantId);
                            continue;
                        }

                        _ = ProcessTenantAsync(tenantId, stoppingToken);
                    }

                    await Task.Delay(RunInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Reconciliation cycle hosted service cancellation requested");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled exception in reconciliation cycle");
                    await Task.Delay(RunInterval, stoppingToken);
                }
            }

            _logger.LogInformation("Reconciliation cycle hosted service stopped");
        }

        private async Task ProcessTenantAsync(Guid tenantId, CancellationToken cancellationToken)
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

                await RunWorkerAsync(scope, tenantId, tenantDb, "Level1",
                    w => w.GetRequiredService<OperationalMatchWorker>().ExecuteAsync(tenantId, tenantDb, cancellationToken),
                    cancellationToken);

                await RunWorkerAsync(scope, tenantId, tenantDb, "Level2",
                    w => w.GetRequiredService<PosErpSyncAuditWorker>().ExecuteAsync(tenantId, tenantDb, cancellationToken),
                    cancellationToken);

                await RunWorkerAsync(scope, tenantId, tenantDb, "Level3",
                    w => w.GetRequiredService<ErpGatewaySalesMatchWorker>().ExecuteAsync(tenantId, tenantDb, cancellationToken),
                    cancellationToken);

                await RunWorkerAsync(scope, tenantId, tenantDb, "Level4",
                    async w =>
                    {
                        var result = await w.GetRequiredService<BankStatementReconciliationWorker>()
                            .ExecuteAsync(tenantId, tenantDb, cancellationToken);
                        return new MatchingRunResult("Level4", result.NeedsBankMatchCount, result.AutoMatchedCount, result.ExceptionCount, result.NoMatchCount);
                    },
                    cancellationToken);

                await RunWorkerAsync(scope, tenantId, tenantDb, "Level6",
                    w => w.GetRequiredService<SettlementMatchWorker>().ExecuteAsync(tenantId, tenantDb, cancellationToken),
                    cancellationToken);

                await RunWorkerAsync(scope, tenantId, tenantDb, "Level7",
                    async w =>
                    {
                        var result = await w.GetRequiredService<PosSettlementMatchWorker>()
                            .ExecuteAsync(tenantId, tenantDb, cancellationToken);
                        var totalMatched = result.Tier1Matched + result.Tier2Matched + result.Tier3Matched;
                        return new MatchingRunResult("Level7", result.TotalCandidates, totalMatched, result.ExceptionCount, result.NoMatchCount);
                    },
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Reconciliation cycle failed for tenant {TenantId}", tenantId);
            }
            finally
            {
                lock (RunningTenants)
                {
                    RunningTenants[tenantId] = false;
                }
            }
        }

        private async Task RunWorkerAsync(
            IServiceScope scope,
            Guid tenantId,
            TenantDbContext tenantDb,
            string level,
            Func<IServiceProvider, Task<MatchingRunResult>> runAsync,
            CancellationToken cancellationToken)
        {
            try
            {
                var result = await runAsync(scope.ServiceProvider);
                _logger.LogInformation(
                    "{Level} completed for tenant {TenantId}: matched={Matched}, exceptions={Exceptions}, noMatch={NoMatch}",
                    level, tenantId, result.AutoMatchedCount, result.ExceptionCount, result.NoMatchCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Level} failed for tenant {TenantId}", level, tenantId);
            }
        }
    }
}
