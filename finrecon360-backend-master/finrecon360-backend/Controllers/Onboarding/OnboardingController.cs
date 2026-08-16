using finrecon360_backend.Data;
using finrecon360_backend.Dtos.Onboarding;
using finrecon360_backend.Models;
using finrecon360_backend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace finrecon360_backend.Controllers.Onboarding
{
    [ApiController]
    [Route("api/onboarding")]
    public class OnboardingController : ControllerBase
    {
        private readonly AppDbContext _dbContext;
        private readonly IOnboardingMagicLinkService _magicLinkService;
        private readonly IOnboardingTokenService _tokenService;
        private readonly IPasswordHasher _passwordHasher;
        private readonly IPaymentCheckoutService _paymentCheckoutService;
        private readonly IWebHostEnvironment _environment;
        private readonly IConfiguration _configuration;
        private readonly IAuditLogger _auditLogger;

        public OnboardingController(
            AppDbContext dbContext,
            IOnboardingMagicLinkService magicLinkService,
            IOnboardingTokenService tokenService,
            IPasswordHasher passwordHasher,
            IPaymentCheckoutService paymentCheckoutService,
            IWebHostEnvironment environment,
            IConfiguration configuration,
            IAuditLogger auditLogger)
        {
            _dbContext = dbContext;
            _magicLinkService = magicLinkService;
            _tokenService = tokenService;
            _passwordHasher = passwordHasher;
            _paymentCheckoutService = paymentCheckoutService;
            _environment = environment;
            _configuration = configuration;
            _auditLogger = auditLogger;
        }

        [HttpPost("magic-link/verify")]
        public async Task<ActionResult<OnboardingMagicLinkVerifyResponse>> VerifyMagicLink([FromBody] OnboardingMagicLinkVerifyRequest request)
        {
            var validation = await _magicLinkService.ValidateTokenAsync(request.Token, MagicLinkPurpose.TenantOnboarding);
            if (!validation.Success || validation.UserId == null)
            {
                return BadRequest(new { message = "Invalid or expired token." });
            }

            var user = await _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == validation.UserId.Value);
            if (user == null)
            {
                return BadRequest(new { message = "Invalid or expired token." });
            }

            var tenant = await _dbContext.TenantUsers
                .AsNoTracking()
                .Where(tu => tu.UserId == user.UserId && tu.Role == TenantUserRole.TenantAdmin)
                .Select(tu => tu.Tenant)
                .FirstOrDefaultAsync();

            if (tenant == null)
            {
                return BadRequest(new { message = "Tenant not found." });
            }

            if (tenant.Status == TenantStatus.Suspended || tenant.Status == TenantStatus.Banned)
            {
                return BadRequest(new { message = "Tenant is not eligible for onboarding." });
            }

            await _auditLogger.LogAsync(user.UserId, "OnboardingMagicLinkVerified", "Tenant", tenant.TenantId.ToString(), null);

            var onboardingToken = _tokenService.CreateToken(user.UserId, tenant.TenantId, user.Email);
            var expires = DateTime.UtcNow.AddMinutes(20);

            return Ok(new OnboardingMagicLinkVerifyResponse(
                onboardingToken,
                user.Email,
                tenant.TenantId,
                tenant.Name,
                expires));
        }

        [HttpPost("set-password")]
        public async Task<IActionResult> SetPassword([FromBody] OnboardingSetPasswordRequest request)
        {
            if (request.Password != request.ConfirmPassword)
            {
                return BadRequest(new { message = "Passwords do not match." });
            }

            if (string.IsNullOrWhiteSpace(request.MagicLinkToken))
            {
                return BadRequest(new { message = "Missing onboarding magic link token." });
            }

            var result = _tokenService.ValidateToken(request.OnboardingToken);
            if (!result.Success || result.UserId == null)
            {
                return BadRequest(new { message = "Invalid or expired onboarding token." });
            }

            var consume = await _magicLinkService.ConsumeTokenAsync(request.MagicLinkToken, MagicLinkPurpose.TenantOnboarding);
            if (!consume.Success || consume.UserId != result.UserId)
            {
                return BadRequest(new { message = "Invalid or expired token." });
            }

            var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.UserId == result.UserId.Value);
            if (user == null)
            {
                return BadRequest(new { message = "Invalid or expired onboarding token." });
            }

            if (user.Status == UserStatus.Suspended || user.Status == UserStatus.Banned)
            {
                return BadRequest(new { message = "User is not eligible for onboarding." });
            }

            user.PasswordHash = _passwordHasher.Hash(request.Password);
            user.Status = UserStatus.Active;
            user.EmailConfirmed = true;
            user.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();
            await _auditLogger.LogAsync(user.UserId, "OnboardingPasswordSet", "User", user.UserId.ToString(), null);

            return Ok(new { message = "Password set." });
        }

        [HttpPost("subscriptions/checkout")]
        public async Task<ActionResult<OnboardingCheckoutResponse>> CreateCheckout([FromBody] OnboardingCheckoutRequest request)
        {
            var tokenResult = _tokenService.ValidateToken(request.OnboardingToken);
            if (!tokenResult.Success || tokenResult.UserId == null || tokenResult.TenantId == null)
            {
                return BadRequest(new { message = "Invalid or expired onboarding token." });
            }

            var user = await _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == tokenResult.UserId.Value);
            if (user == null || user.Status != UserStatus.Active)
            {
                return BadRequest(new { message = "User onboarding is incomplete." });
            }

            var plan = await _dbContext.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.PlanId == request.PlanId && p.IsActive);
            if (plan == null)
            {
                return BadRequest(new { message = "Plan not found." });
            }

            var tenant = await _dbContext.Tenants.FirstOrDefaultAsync(t => t.TenantId == tokenResult.TenantId);
            if (tenant == null)
            {
                return BadRequest(new { message = "Tenant not found." });
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
            await _dbContext.SaveChangesAsync();

            if (!_paymentCheckoutService.IsConfigured())
            {
                var allowLocalBypass = _configuration.GetValue<bool>("PAYMENT_ALLOW_LOCAL_BYPASS", false);
                if (_environment.IsProduction() || !allowLocalBypass)
                {
                    await _auditLogger.LogAsync(
                        tokenResult.UserId.Value,
                        "OnboardingCheckoutBlocked",
                        "Subscription",
                        subscription.SubscriptionId.ToString(),
                        "Payment provider is not configured and bypass is disabled.");

                    return StatusCode(StatusCodes.Status503ServiceUnavailable, new
                    {
                        message = "Payment service is not configured. Please contact support."
                    });
                }

                var now = DateTime.UtcNow;
                subscription.Status = SubscriptionStatus.Active;
                subscription.CurrentPeriodStart = now;
                subscription.CurrentPeriodEnd = now.AddDays(plan.DurationDays);

                tenant.Status = TenantStatus.Active;
                tenant.ActivatedAt = now;
                tenant.CurrentSubscriptionId = subscription.SubscriptionId;

                await _dbContext.SaveChangesAsync();
                await _auditLogger.LogAsync(
                    tokenResult.UserId.Value,
                    "OnboardingCheckoutBypassed",
                    "Subscription",
                    subscription.SubscriptionId.ToString(),
                    $"{_paymentCheckoutService.GetProviderName()} not configured; local activation applied.");

                return Ok(new OnboardingCheckoutResponse(_paymentCheckoutService.GetFallbackCheckoutUrl()));
            }

            var session = await _paymentCheckoutService.CreateCheckoutSessionAsync(
                plan.Name,
                plan.PriceCents,
                plan.Currency,
                tenant.TenantId,
                subscription.SubscriptionId,
                tokenResult.UserId.Value);

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
            await _dbContext.SaveChangesAsync();

            await _auditLogger.LogAsync(
                tokenResult.UserId.Value,
                "OnboardingCheckoutCreated",
                "Subscription",
                subscription.SubscriptionId.ToString(),
                $"provider={session.Provider};sessionId={session.SessionId}");

            return Ok(new OnboardingCheckoutResponse(session.CheckoutUrl));
        }
    }
}
