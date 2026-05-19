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
    /// in JournalReady state and automatically posts journal entries to the GL.
    /// 
    /// Behavior:
    /// - Runs on an interval (default: every 5 minutes, staggered after bank reconciliation)
    /// - For each active tenant, executes one cycle of journal posting
    /// - Safely handles concurrent execution; skips tenants if a cycle is already running
    /// - Logs all activities and exceptions
    /// </summary>
    public class JournalPostingHostedService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<JournalPostingHostedService> _logger;

        private static readonly TimeSpan RunInterval = TimeSpan.FromMinutes(5);
        private static readonly Dictionary<Guid, bool> RunningTenants = new();

        public JournalPostingHostedService(
            IServiceScopeFactory scopeFactory,
            ILogger<JournalPostingHostedService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Journal posting hosted service started");

            // Delay startup to allow application to fully initialize + let bank reconciliation run first
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogDebug("Starting journal posting cycle");

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
                            _logger.LogDebug("Skipping tenant {TenantId}; journal posting cycle already in progress", tenantId);
                            continue;
                        }

                        _ = ProcessTenantAsync(tenantId, stoppingToken);
                    }

                    await Task.Delay(RunInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Journal posting hosted service cancellation requested");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled exception in journal posting cycle");
                    await Task.Delay(RunInterval, stoppingToken);
                }
            }

            _logger.LogInformation("Journal posting hosted service stopped");
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
                var worker = scope.ServiceProvider.GetRequiredService<IJournalPostingExecutorWorker>();

                await using var tenantDb = await tenantDbContextFactory.CreateAsync(tenantId, cancellationToken);
                var result = await worker.ExecuteAsync(tenantId, tenantDb, cancellationToken);
                _logger.LogInformation("Journal posting completed for tenant {TenantId}: {Summary}",
                    tenantId, result.Summary);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Journal posting failed for tenant {TenantId}", tenantId);
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
