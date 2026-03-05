using finrecon360_backend.Data;
using finrecon360_backend.Models;
using finrecon360_backend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stripe;
using System.IO;

namespace finrecon360_backend.Controllers.Webhooks
{
    [ApiController]
    [Route("api/webhooks/stripe")]
    public class StripeWebhooksController : ControllerBase
    {
        private readonly AppDbContext _dbContext;
        private readonly IStripeCheckoutService _stripeCheckoutService;
        private readonly IAuditLogger _auditLogger;
        private readonly ITenantUserDirectoryService _tenantUserDirectoryService;

        public StripeWebhooksController(
            AppDbContext dbContext,
            IStripeCheckoutService stripeCheckoutService,
            IAuditLogger auditLogger,
            ITenantUserDirectoryService tenantUserDirectoryService)
        {
            _dbContext = dbContext;
            _stripeCheckoutService = stripeCheckoutService;
            _auditLogger = auditLogger;
            _tenantUserDirectoryService = tenantUserDirectoryService;
        }

        [HttpPost]
        public async Task<IActionResult> Handle()
        {
            using var reader = new StreamReader(HttpContext.Request.Body);
            var payload = await reader.ReadToEndAsync();
            var signature = Request.Headers["Stripe-Signature"].ToString();

            Event stripeEvent;
            try
            {
                stripeEvent = _stripeCheckoutService.ParseWebhookEvent(payload, signature);
            }
            catch
            {
                return BadRequest();
            }

            if (stripeEvent.Type == Events.CheckoutSessionCompleted)
            {
                var session = stripeEvent.Data.Object as Stripe.Checkout.Session;
                if (session != null)
                {
                    await HandleCheckoutCompleted(session);
                }
            }

            if (stripeEvent.Type == Events.CheckoutSessionExpired)
            {
                var session = stripeEvent.Data.Object as Stripe.Checkout.Session;
                if (session != null)
                {
                    await HandleCheckoutFailed(session.Id);
                }
            }

            return Ok();
        }

        private async Task HandleCheckoutCompleted(Stripe.Checkout.Session session)
        {
            var payment = await _dbContext.PaymentSessions
                .Include(p => p.Subscription)
                .ThenInclude(s => s.Plan)
                .FirstOrDefaultAsync(p => p.StripeSessionId == session.Id);

            if (payment == null)
            {
                return;
            }

            if (payment.Status == PaymentSessionStatus.Paid)
            {
                return;
            }

            payment.Status = PaymentSessionStatus.Paid;
            payment.PaidAt = DateTime.UtcNow;

            var subscription = payment.Subscription;
            subscription.Status = SubscriptionStatus.Active;
            subscription.CurrentPeriodStart = DateTime.UtcNow;
            subscription.CurrentPeriodEnd = DateTime.UtcNow.AddDays(subscription.Plan.DurationDays);

            var tenant = await _dbContext.Tenants.FirstOrDefaultAsync(t => t.TenantId == payment.TenantId);
            if (tenant != null)
            {
                tenant.Status = TenantStatus.Active;
                tenant.ActivatedAt ??= DateTime.UtcNow;
                tenant.CurrentSubscriptionId = subscription.SubscriptionId;
            }

            if (session.Metadata != null && session.Metadata.TryGetValue("userId", out var userIdValue))
            {
                if (Guid.TryParse(userIdValue, out var userId))
                {
                    var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.UserId == userId);
                    var adminExists = await _dbContext.TenantUsers
                        .AnyAsync(tu => tu.TenantId == payment.TenantId && tu.Role == TenantUserRole.TenantAdmin);

                    if (!adminExists)
                    {
                        _dbContext.TenantUsers.Add(new TenantUser
                        {
                            TenantUserId = Guid.NewGuid(),
                            TenantId = payment.TenantId,
                            UserId = userId,
                            Role = TenantUserRole.TenantAdmin,
                            CreatedAt = DateTime.UtcNow
                        });
                    }

                    if (user != null)
                    {
                        await _tenantUserDirectoryService.UpsertTenantUserAsync(payment.TenantId, user, TenantUserRole.TenantAdmin);
                    }
                }
            }

            await _dbContext.SaveChangesAsync();
            await _auditLogger.LogAsync(null, "SubscriptionActivated", "Subscription", subscription.SubscriptionId.ToString(), null);
        }

        private async Task HandleCheckoutFailed(string sessionId)
        {
            var payment = await _dbContext.PaymentSessions.FirstOrDefaultAsync(p => p.StripeSessionId == sessionId);
            if (payment == null)
            {
                return;
            }

            payment.Status = PaymentSessionStatus.Failed;
            await _dbContext.SaveChangesAsync();
        }
    }
}
