using finrecon360_backend.Data;
using finrecon360_backend.Models;
using finrecon360_backend.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace finrecon360_backend.BackgroundServices
{
    /// <summary>
    /// WHY: The only source of truth for "this tenant has missed a payment". It flips overdue
    /// subscriptions to PastDue (a status that existed on the enum but nothing ever set) and
    /// raises a TenantPaymentAlert the moment a subscription's period lapses, so the tenant and
    /// system admins both find out immediately. If the tenant still hasn't paid once the grace
    /// period (SystemBillingSettings.PaymentOverdueSuspensionThresholdDays, default 7) elapses,
    /// this service — and only this service — automatically suspends the tenant. That is a
    /// deliberate exception to "enforcement is a human decision": non-payment past a fixed,
    /// disclosed grace period is an objective billing rule, not a judgment call, and it is
    /// symmetric — PayHereWebhooksController reactivates the tenant the moment they pay, and a
    /// suspended (not banned) tenant can still reach their own billing page to do so.
    /// </summary>
    public class SubscriptionOverdueMonitorHostedService : BackgroundService
    {
        private static readonly TimeSpan RunInterval = TimeSpan.FromHours(1);
        private const string AutoSuspendReason = "Automatic suspension: subscription payment overdue past the grace period.";

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<SubscriptionOverdueMonitorHostedService> _logger;

        public SubscriptionOverdueMonitorHostedService(
            IServiceScopeFactory scopeFactory,
            ILogger<SubscriptionOverdueMonitorHostedService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Subscription overdue monitor started");

            await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);

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
                    _logger.LogError(ex, "Unhandled exception in subscription overdue monitor cycle");
                }

                await Task.Delay(RunInterval, stoppingToken);
            }

            _logger.LogInformation("Subscription overdue monitor stopped");
        }

        private async Task RunCycleAsync(CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var auditLogger = scope.ServiceProvider.GetRequiredService<IAuditLogger>();

            var now = DateTime.UtcNow;
            var suspensionThresholdDays = await db.SystemBillingSettings.AsNoTracking()
                .Select(s => (int?)s.PaymentOverdueSuspensionThresholdDays)
                .FirstOrDefaultAsync(ct) ?? 7;

            var overdueSubscriptions = await db.Subscriptions
                .Include(s => s.Tenant)
                .Where(s =>
                    (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.PastDue) &&
                    s.CurrentPeriodEnd != null && s.CurrentPeriodEnd < now)
                .ToListAsync(ct);

            if (overdueSubscriptions.Count == 0)
            {
                return;
            }

            var subscriptionIds = overdueSubscriptions.Select(s => s.SubscriptionId).ToList();
            var openAlerts = await db.TenantPaymentAlerts
                .Where(a => subscriptionIds.Contains(a.SubscriptionId) && a.Status == PaymentAlertStatus.Open)
                .ToListAsync(ct);
            var openAlertsBySubscription = openAlerts.ToDictionary(a => a.SubscriptionId);

            foreach (var subscription in overdueSubscriptions)
            {
                subscription.Status = SubscriptionStatus.PastDue;
                var daysOverdue = (int)(now - subscription.CurrentPeriodEnd!.Value).TotalDays;

                if (openAlertsBySubscription.TryGetValue(subscription.SubscriptionId, out var existingAlert))
                {
                    existingAlert.DaysOverdue = daysOverdue;
                }
                else
                {
                    db.TenantPaymentAlerts.Add(new TenantPaymentAlert
                    {
                        TenantPaymentAlertId = Guid.NewGuid(),
                        TenantId = subscription.TenantId,
                        SubscriptionId = subscription.SubscriptionId,
                        PeriodEndAt = subscription.CurrentPeriodEnd.Value,
                        DaysOverdue = daysOverdue,
                        Status = PaymentAlertStatus.Open,
                        CreatedAt = now,
                    });
                    _logger.LogInformation(
                        "Raised payment alert for tenant {TenantId}, subscription {SubscriptionId}, {DaysOverdue} days overdue",
                        subscription.TenantId, subscription.SubscriptionId, daysOverdue);
                }

                if (daysOverdue >= suspensionThresholdDays && subscription.Tenant.Status == TenantStatus.Active)
                {
                    subscription.Tenant.Status = TenantStatus.Suspended;

                    db.EnforcementActions.Add(new EnforcementAction
                    {
                        EnforcementActionId = Guid.NewGuid(),
                        TargetType = EnforcementTargetType.Tenant,
                        TargetId = subscription.TenantId,
                        ActionType = EnforcementActionType.Suspend,
                        Reason = AutoSuspendReason,
                        // No human actor triggered this — Guid.Empty marks it as system-originated
                        // rather than attributing it to whichever admin happened to be logged in.
                        CreatedBy = Guid.Empty,
                        CreatedAt = now,
                    });

                    _logger.LogWarning(
                        "Auto-suspended tenant {TenantId} for non-payment: {DaysOverdue} days overdue (threshold {ThresholdDays})",
                        subscription.TenantId, daysOverdue, suspensionThresholdDays);

                    await auditLogger.LogAsync(null, "TenantAutoSuspendedForNonPayment", "Tenant", subscription.TenantId.ToString(), AutoSuspendReason);
                }
            }

            await db.SaveChangesAsync(ct);
        }
    }
}
