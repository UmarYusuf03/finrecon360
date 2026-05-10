using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using finrecon360_backend.Data;
using finrecon360_backend.Services;
using finrecon360_backend.Services.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace finrecon360_backend.BackgroundServices
{
    public class ReportingSnapshotHostedService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ReportingSnapshotHostedService> _logger;
        private static readonly TimeSpan RunInterval = TimeSpan.FromHours(24);

        public ReportingSnapshotHostedService(
            IServiceScopeFactory scopeFactory,
            ILogger<ReportingSnapshotHostedService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("ReportingSnapshotHostedService started");

            // Delay startup to allow application to fully initialize
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogInformation("Starting KPI reporting snapshot cycle");

                    using var scope = _scopeFactory.CreateScope();
                    var appDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                    var activeTenants = await appDb.Tenants
                        .AsNoTracking()
                        .Where(t => t.Status == finrecon360_backend.Models.TenantStatus.Active)
                        .Select(t => t.TenantId)
                        .ToListAsync(stoppingToken);

                    foreach (var tenantId in activeTenants)
                    {
                        await ProcessTenantAsync(tenantId, stoppingToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("ReportingSnapshotHostedService cancellation requested");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled exception in KPI reporting snapshot cycle");
                }

                // Wait for the next run (e.g. daily)
                await Task.Delay(RunInterval, stoppingToken);
            }

            _logger.LogInformation("ReportingSnapshotHostedService stopped");
        }

        private async Task ProcessTenantAsync(Guid tenantId, CancellationToken cancellationToken)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var tenantDbContextFactory = scope.ServiceProvider.GetRequiredService<ITenantDbContextFactory>();
                var worker = scope.ServiceProvider.GetRequiredService<IReportingSnapshotWorker>();

                await using var tenantDb = await tenantDbContextFactory.CreateAsync(tenantId, cancellationToken);
                await worker.ExecuteAsync(tenantId, tenantDb, cancellationToken);
                
                _logger.LogInformation("Reporting snapshot cycle completed for tenant {TenantId}", tenantId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Reporting snapshot cycle failed for tenant {TenantId}", tenantId);
            }
        }
    }
}
