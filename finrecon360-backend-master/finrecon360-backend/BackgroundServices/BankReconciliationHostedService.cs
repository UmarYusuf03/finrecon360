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
    /// WHY: Background task that continuously monitors tenant databases for transactions
    /// in NeedsBankMatch state and automatically reconciles them with bank statement records
    /// when high-confidence matches are found.
    /// 
    /// Behavior:
    /// - Runs on an interval (default: every 5 minutes)
    /// - For each active tenant, executes one cycle of bank reconciliation
    /// - Safely handles concurrent execution; skips tenants if a cycle is already running
    /// - Logs all activities and exceptions
    /// </summary>
    public class BankReconciliationHostedService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<BankReconciliationHostedService> _logger;

        private static readonly TimeSpan RunInterval = TimeSpan.FromMinutes(5);
        private static readonly Dictionary<Guid, bool> RunningTenants = new();

        public BankReconciliationHostedService(
            IServiceScopeFactory scopeFactory,
            ILogger<BankReconciliationHostedService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Bank reconciliation hosted service started");

            // Delay startup to allow application to fully initialize
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogDebug("Starting bank reconciliation cycle");

                    // Find all active tenants
                    using var scope = _scopeFactory.CreateScope();
                    var appDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                    var activeTenants = appDb.Tenants
                        .AsNoTracking()
                        .Where(t => t.Status == TenantStatus.Active)
                        .Select(t => t.TenantId)
                        .ToList();

                    foreach (var tenantId in activeTenants)
                    {
                        // Skip if already running for this tenant
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
                    _logger.LogInformation("Bank reconciliation hosted service cancellation requested");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled exception in bank reconciliation cycle");
                    await Task.Delay(RunInterval, stoppingToken);
                }
            }

            _logger.LogInformation("Bank reconciliation hosted service stopped");
        }

        private async Task ProcessTenantAsync(Guid tenantId, CancellationToken cancellationToken)
        {
            lock (RunningTenants)
            {
                if (RunningTenants.ContainsKey(tenantId))
                    RunningTenants[tenantId] = true;
                else
                    RunningTenants[tenantId] = true;
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var tenantDbContextFactory = scope.ServiceProvider.GetRequiredService<ITenantDbContextFactory>();
                var worker = scope.ServiceProvider.GetRequiredService<IBankStatementReconciliationWorker>();

                await using var tenantDb = await tenantDbContextFactory.CreateAsync(tenantId, cancellationToken);
                var result = await worker.ExecuteAsync(tenantId, tenantDb, cancellationToken);
                _logger.LogInformation("Bank reconciliation completed for tenant {TenantId}: {Summary}", 
                    tenantId, result.Summary);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Bank reconciliation failed for tenant {TenantId}", tenantId);
            }
            finally
            {
                lock (RunningTenants)
                {
                    if (RunningTenants.ContainsKey(tenantId))
                        RunningTenants[tenantId] = false;
                }
            }
        }
    }
}
