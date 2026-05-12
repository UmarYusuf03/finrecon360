using finrecon360_backend.Data;
using finrecon360_backend.Dtos.Admin;
using finrecon360_backend.Dtos.Public;
using finrecon360_backend.Dtos.Subscriptions;
using finrecon360_backend.Models;
using Microsoft.EntityFrameworkCore;

namespace finrecon360_backend.Services
{
    public interface ISubscriptionService
    {
        Task<SubscriptionOverviewDto> GetOverviewAsync(Guid tenantId, CancellationToken cancellationToken = default);
        Task<SubscriptionCheckoutResponse> CreateCheckoutAsync(Guid tenantId, Guid userId, Guid planId, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// WHY: Centralizes tenant subscription selection and plan-change checkout creation so onboarding,
    /// tenant self-service, and system-admin support flows all share the same subscription lifecycle rules.
    /// </summary>
    public class SubscriptionService : ISubscriptionService
    {
        private readonly AppDbContext _dbContext;
        private readonly ITenantDbContextFactory _tenantDbContextFactory;
        private readonly IPaymentCheckoutService _paymentCheckoutService;
        private readonly IWebHostEnvironment _environment;
        private readonly IConfiguration _configuration;
        private readonly IAuditLogger _auditLogger;

        public SubscriptionService(
            AppDbContext dbContext,
            ITenantDbContextFactory tenantDbContextFactory,
            IPaymentCheckoutService paymentCheckoutService,
            IWebHostEnvironment environment,
            IConfiguration configuration,
            IAuditLogger auditLogger)
        {
            _dbContext = dbContext;
            _tenantDbContextFactory = tenantDbContextFactory;
            _paymentCheckoutService = paymentCheckoutService;
            _environment = environment;
            _configuration = configuration;
            _auditLogger = auditLogger;
        }

        public async Task<SubscriptionOverviewDto> GetOverviewAsync(Guid tenantId, CancellationToken cancellationToken = default)
        {
            var tenant = await _dbContext.Tenants
                .AsNoTracking()
                .Include(t => t.CurrentSubscription)
                .ThenInclude(s => s!.Plan)
                .FirstOrDefaultAsync(t => t.TenantId == tenantId, cancellationToken);

            if (tenant == null)
            {
                throw new InvalidOperationException("Tenant not found.");
            }

            var currentSubscription = tenant.CurrentSubscription == null
                ? null
                : new TenantSubscriptionDto(
                    tenant.CurrentSubscription.SubscriptionId,
                    tenant.CurrentSubscription.Plan.Code,
                    tenant.CurrentSubscription.Plan.Name,
                    tenant.CurrentSubscription.Status.ToString(),
                    tenant.CurrentSubscription.CurrentPeriodStart,
                    tenant.CurrentSubscription.CurrentPeriodEnd);

            var plans = await _dbContext.Plans
                .AsNoTracking()
                .Where(p => p.IsActive)
                .OrderBy(p => p.PriceCents)
                .Select(p => new PublicPlanSummaryDto(
                    p.PlanId,
                    p.Code,
                    p.Name,
                    p.PriceCents,
                    p.Currency,
                    p.DurationDays,
                    p.MaxUsers,
                    p.MaxAccounts))
                .ToListAsync(cancellationToken);

            return new SubscriptionOverviewDto(currentSubscription, plans);
        }

        public async Task<SubscriptionCheckoutResponse> CreateCheckoutAsync(Guid tenantId, Guid userId, Guid planId, CancellationToken cancellationToken = default)
        {
            var tenant = await _dbContext.Tenants.FirstOrDefaultAsync(t => t.TenantId == tenantId, cancellationToken);
            if (tenant == null)
            {
                throw new InvalidOperationException("Tenant not found.");
            }

            if (tenant.Status != TenantStatus.Active)
            {
                throw new InvalidOperationException("Tenant subscription changes are only available for active tenants.");
            }

            var plan = await _dbContext.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.PlanId == planId && p.IsActive, cancellationToken);
            if (plan == null)
            {
                throw new InvalidOperationException("Plan not found.");
            }

            var currentUser = await _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId, cancellationToken);
            if (currentUser == null || !currentUser.IsActive)
            {
                throw new InvalidOperationException("Current user is not available.");
            }

            await using var tenantDb = await _tenantDbContextFactory.CreateAsync(tenantId, cancellationToken);

            var activeUsers = await tenantDb.TenantUsers
                .AsNoTracking()
                .CountAsync(tu => tu.IsActive, cancellationToken);

            if (activeUsers > plan.MaxUsers)
            {
                throw new InvalidOperationException($"Selected plan allows {plan.MaxUsers} users, but the tenant already has {activeUsers} active users.");
            }

            var activeAccounts = await tenantDb.BankAccounts
                .AsNoTracking()
                .CountAsync(bankAccount => bankAccount.IsActive, cancellationToken);

            if (activeAccounts > plan.MaxAccounts)
            {
                throw new InvalidOperationException($"Selected plan allows {plan.MaxAccounts} bank accounts, but the tenant already has {activeAccounts} active bank accounts.");
            }

            var subscription = new Subscription
            {
                SubscriptionId = Guid.NewGuid(),
                TenantId = tenant.TenantId,
                PlanId = plan.PlanId,
                Status = SubscriptionStatus.PendingPayment,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.Subscriptions.Add(subscription);
            await _dbContext.SaveChangesAsync(cancellationToken);

            var allowLocalBypass = _configuration.GetValue<bool>("PAYMENT_ALLOW_LOCAL_BYPASS", false);
            if (allowLocalBypass && !_environment.IsProduction())
            {
                ActivateSubscriptionImmediately(tenant, subscription, plan);
                await _dbContext.SaveChangesAsync(cancellationToken);

                await _auditLogger.LogAsync(
                    userId,
                    "SubscriptionChangeBypassed",
                    "Subscription",
                    subscription.SubscriptionId.ToString(),
                    "PAYMENT_ALLOW_LOCAL_BYPASS enabled; local activation applied.");

                return new SubscriptionCheckoutResponse(subscription.SubscriptionId, _paymentCheckoutService.GetFallbackCheckoutUrl());
            }

            if (!_paymentCheckoutService.IsConfigured())
            {
                if (_environment.IsProduction() || !allowLocalBypass)
                {
                    await _auditLogger.LogAsync(
                        userId,
                        "SubscriptionChangeBlocked",
                        "Subscription",
                        subscription.SubscriptionId.ToString(),
                        "Payment provider is not configured and bypass is disabled.");

                    throw new InvalidOperationException("Payment service is not configured. Please contact support.");
                }

                ActivateSubscriptionImmediately(tenant, subscription, plan);
                await _dbContext.SaveChangesAsync(cancellationToken);

                await _auditLogger.LogAsync(
                    userId,
                    "SubscriptionChangeBypassed",
                    "Subscription",
                    subscription.SubscriptionId.ToString(),
                    $"{_paymentCheckoutService.GetProviderName()} not configured; local activation applied.");

                return new SubscriptionCheckoutResponse(subscription.SubscriptionId, _paymentCheckoutService.GetFallbackCheckoutUrl());
            }

            var session = await _paymentCheckoutService.CreateCheckoutSessionAsync(
                plan.Name,
                plan.PriceCents,
                plan.Currency,
                tenant.TenantId,
                subscription.SubscriptionId,
                userId,
                tenant.Name,
                currentUser.Email,
                string.IsNullOrWhiteSpace(currentUser.PhoneNumber) ? "0000000000" : currentUser.PhoneNumber.Trim(),
                cancellationToken);

            var paymentSession = new PaymentSession
            {
                PaymentSessionId = Guid.NewGuid(),
                TenantId = tenant.TenantId,
                SubscriptionId = subscription.SubscriptionId,
                ProviderSessionId = session.SessionId,
                ProviderReferenceId = session.CustomerId,
                Status = PaymentSessionStatus.Created,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.PaymentSessions.Add(paymentSession);
            await _dbContext.SaveChangesAsync(cancellationToken);

            await _auditLogger.LogAsync(
                userId,
                "SubscriptionChangeCheckoutCreated",
                "Subscription",
                subscription.SubscriptionId.ToString(),
                $"provider={session.Provider};sessionId={session.SessionId}");

            return new SubscriptionCheckoutResponse(subscription.SubscriptionId, session.CheckoutUrl);
        }

        private static void ActivateSubscriptionImmediately(Tenant tenant, Subscription subscription, Plan plan)
        {
            var now = DateTime.UtcNow;

            subscription.Status = SubscriptionStatus.Active;
            subscription.CurrentPeriodStart = now;
            subscription.CurrentPeriodEnd = now.AddDays(plan.DurationDays);

            tenant.Status = TenantStatus.Active;
            tenant.ActivatedAt ??= now;
            tenant.CurrentSubscriptionId = subscription.SubscriptionId;
        }
    }
}