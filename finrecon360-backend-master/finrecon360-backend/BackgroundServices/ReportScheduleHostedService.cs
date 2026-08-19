using finrecon360_backend.Data;
using finrecon360_backend.Models;
using finrecon360_backend.Services;
using finrecon360_backend.Services.Export;
using finrecon360_backend.Services.Reporting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace finrecon360_backend.BackgroundServices
{
    /// <summary>
    /// WHY: Checks every active tenant's ReportSchedule rows hourly and emails out any that are
    /// due (NextRunAt &lt;= now), rendering via IScheduledReportRenderer/IReportExporter (Phase 0)
    /// and sending through IEmailSender.SendWithAttachmentAsync. An hourly cadence against a
    /// fixed per-schedule delivery hour (06:00 UTC, see ComputeNextRunAt) means a schedule fires
    /// within an hour of its target time, not to-the-minute — acceptable for a weekly email digest.
    /// </summary>
    public class ReportScheduleHostedService : BackgroundService
    {
        private const int DeliveryHourUtc = 6;
        private static readonly TimeSpan RunInterval = TimeSpan.FromHours(1);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ReportScheduleHostedService> _logger;

        public ReportScheduleHostedService(IServiceScopeFactory scopeFactory, ILogger<ReportScheduleHostedService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public static DateTime ComputeNextRunAt(DateTime fromUtc, DayOfWeek dayOfWeek)
        {
            var candidate = new DateTime(fromUtc.Year, fromUtc.Month, fromUtc.Day, DeliveryHourUtc, 0, 0, DateTimeKind.Utc);
            var daysUntilTarget = ((int)dayOfWeek - (int)candidate.DayOfWeek + 7) % 7;
            candidate = candidate.AddDays(daysUntilTarget);

            if (candidate <= fromUtc)
            {
                candidate = candidate.AddDays(7);
            }

            return candidate;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Report schedule hosted service started");

            await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunCycleAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled exception in report schedule cycle");
                }

                await Task.Delay(RunInterval, stoppingToken);
            }

            _logger.LogInformation("Report schedule hosted service stopped");
        }

        private async Task RunCycleAsync(CancellationToken ct)
        {
            var now = DateTime.UtcNow;

            using var scope = _scopeFactory.CreateScope();
            var appDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var activeTenants = await appDb.Tenants
                .AsNoTracking()
                .Where(t => t.Status == TenantStatus.Active)
                .Select(t => new { t.TenantId, t.Name })
                .ToListAsync(ct);

            foreach (var tenant in activeTenants)
            {
                try
                {
                    await ProcessTenantAsync(tenant.TenantId, tenant.Name, now, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Report schedule cycle failed for tenant {TenantId}", tenant.TenantId);
                }
            }
        }

        private async Task ProcessTenantAsync(Guid tenantId, string tenantName, DateTime now, CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var tenantDbContextFactory = scope.ServiceProvider.GetRequiredService<ITenantDbContextFactory>();
            var renderer = scope.ServiceProvider.GetRequiredService<IScheduledReportRenderer>();
            var reportExporter = scope.ServiceProvider.GetRequiredService<IReportExporter>();
            var emailSender = scope.ServiceProvider.GetRequiredService<IEmailSender>();

            await using var tenantDb = await tenantDbContextFactory.CreateAsync(tenantId, ct);

            var dueSchedules = await tenantDb.ReportSchedules
                .Where(s => s.IsActive && s.NextRunAt <= now)
                .ToListAsync(ct);

            if (dueSchedules.Count == 0)
            {
                return;
            }

            foreach (var schedule in dueSchedules)
            {
                try
                {
                    if (!reportExporter.TryParseFormat(schedule.Format, out var format))
                    {
                        format = ReportExportFormat.Csv;
                    }

                    var file = await renderer.RenderAsync(tenantDb, schedule.ReportType, format, ct);
                    var fileName = $"{schedule.ReportType}-{now:yyyyMMdd}.{file.FileExtension}";
                    var subject = $"{schedule.ReportType} report — {tenantName} — {now:yyyy-MM-dd}";
                    var body = $"<p>Attached is your scheduled <strong>{schedule.ReportType}</strong> report for <strong>{tenantName}</strong>, generated {now:yyyy-MM-dd HH:mm} UTC.</p>";

                    await emailSender.SendWithAttachmentAsync(
                        schedule.RecipientEmail,
                        subject,
                        body,
                        new[] { new EmailAttachment(fileName, file.Content) },
                        ct);

                    schedule.LastRunAt = now;
                    schedule.NextRunAt = ComputeNextRunAt(now, (DayOfWeek)schedule.DayOfWeek);

                    _logger.LogInformation(
                        "Sent scheduled report {ReportType} for tenant {TenantId} to {Recipient}; next run {NextRunAt}",
                        schedule.ReportType, tenantId, schedule.RecipientEmail, schedule.NextRunAt);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send scheduled report {ReportScheduleId} for tenant {TenantId}",
                        schedule.ReportScheduleId, tenantId);
                    // Push NextRunAt forward regardless of failure so a persistently broken schedule
                    // (bad recipient address, etc.) doesn't retry every hour forever — it gets one
                    // more shot next week, same as a healthy schedule would.
                    schedule.NextRunAt = ComputeNextRunAt(now, (DayOfWeek)schedule.DayOfWeek);
                }
            }

            await tenantDb.SaveChangesAsync(ct);
        }
    }
}
