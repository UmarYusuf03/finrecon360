using finrecon360_backend.Data;
using finrecon360_backend.Models;
using finrecon360_backend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace finrecon360_backend.Controllers.Webhooks
{
    [ApiController]
    [Route("api/webhooks/payhere")]
    public class PayHereWebhooksController : ControllerBase
    {
        private readonly AppDbContext _dbContext;
        private readonly IPayHereCheckoutService _payHereCheckoutService;
        private readonly IWebHostEnvironment _environment;
        private readonly IAuditLogger _auditLogger;
        private readonly ITenantUserDirectoryService _tenantUserDirectoryService;

        public PayHereWebhooksController(
            AppDbContext dbContext,
            IPayHereCheckoutService payHereCheckoutService,
            IWebHostEnvironment environment,
            IAuditLogger auditLogger,
            ITenantUserDirectoryService tenantUserDirectoryService)
        {
            _dbContext = dbContext;
            _payHereCheckoutService = payHereCheckoutService;
            _environment = environment;
            _auditLogger = auditLogger;
            _tenantUserDirectoryService = tenantUserDirectoryService;
        }

        [HttpPost]
        public async Task<IActionResult> Handle()
        {
            if (_environment.IsProduction() && !_payHereCheckoutService.IsConfigured())
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            if (!Request.HasFormContentType)
            {
                return BadRequest();
            }

            var callback = _payHereCheckoutService.ParseCallback(Request.Form);
            if (!callback.IsValid)
            {
                return BadRequest();
            }

            if (callback.IsSuccess)
            {
                await HandlePaymentCompleted(callback.OrderId, callback.PaymentId, callback.UserId);
                return Ok();
            }

            await HandlePaymentFailed(callback.OrderId);
            return Ok();
        }

        private async Task HandlePaymentCompleted(string orderId, string? paymentId, Guid? userId)
        {
            var payment = await _dbContext.PaymentSessions
                .Include(p => p.Subscription)
                .ThenInclude(s => s.Plan)
                .FirstOrDefaultAsync(p => p.ProviderSessionId == orderId);

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
            payment.ProviderReferenceId = paymentId;

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

            if (userId.HasValue)
            {
                var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.UserId == userId.Value);
                if (user != null)
                {
                    if (user.UserType != UserType.SystemAdmin)
                    {
                        if (user.UserType == UserType.GlobalPublic)
                        {
                            user.UserType = UserType.TenantOperational;
                            user.UpdatedAt = DateTime.UtcNow;
                        }

                        var adminExists = await _dbContext.TenantUsers
                            .AnyAsync(tu => tu.TenantId == payment.TenantId && tu.Role == TenantUserRole.TenantAdmin);

                        if (!adminExists)
                        {
                            _dbContext.TenantUsers.Add(new TenantUser
                            {
                                TenantUserId = Guid.NewGuid(),
                                TenantId = payment.TenantId,
                                UserId = user.UserId,
                                Role = TenantUserRole.TenantAdmin,
                                CreatedAt = DateTime.UtcNow
                            });
                        }

                        await _tenantUserDirectoryService.UpsertTenantUserAsync(payment.TenantId, user, TenantUserRole.TenantAdmin);
                    }
                }
            }

            await _dbContext.SaveChangesAsync();
            await _auditLogger.LogAsync(null, "SubscriptionActivated", "Subscription", subscription.SubscriptionId.ToString(), "provider=PayHere");
        }

        private async Task HandlePaymentFailed(string orderId)
        {
            var payment = await _dbContext.PaymentSessions.FirstOrDefaultAsync(p => p.ProviderSessionId == orderId);
            if (payment == null)
            {
                return;
            }

            payment.Status = PaymentSessionStatus.Failed;
            await _dbContext.SaveChangesAsync();
        }
    }
}
