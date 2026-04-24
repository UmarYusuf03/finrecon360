using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using finrecon360_backend.Data;
using finrecon360_backend.Dtos.Onboarding;
using finrecon360_backend.Models;
using finrecon360_backend.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace finrecon360_backend.Tests;

public class PayHereIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public PayHereIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PayHere_webhook_valid_signature_activates_subscription_and_tenant()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();
        var orderId = subscriptionId.ToString("N");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Users.Add(new User
            {
                UserId = userId,
                Email = "payhere-user@test.local",
                FirstName = "Pay",
                LastName = "Here",
                Country = "LK",
                Gender = "NA",
                PasswordHash = "hash",
                CreatedAt = DateTime.UtcNow,
                IsActive = true,
                Status = UserStatus.Active,
                UserType = UserType.GlobalPublic
            });

            db.Tenants.Add(new Tenant
            {
                TenantId = tenantId,
                Name = "PayHere Tenant",
                Status = TenantStatus.Pending,
                CreatedAt = DateTime.UtcNow
            });

            db.Plans.Add(new Plan
            {
                PlanId = planId,
                Code = "PH-01",
                Name = "PayHere Plan",
                PriceCents = 15000,
                Currency = "LKR",
                DurationDays = 30,
                MaxUsers = 10,
                MaxAccounts = 3,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });

            db.Subscriptions.Add(new Subscription
            {
                SubscriptionId = subscriptionId,
                TenantId = tenantId,
                PlanId = planId,
                Status = SubscriptionStatus.PendingPayment,
                CreatedAt = DateTime.UtcNow
            });

            db.PaymentSessions.Add(new PaymentSession
            {
                PaymentSessionId = Guid.NewGuid(),
                TenantId = tenantId,
                SubscriptionId = subscriptionId,
                ProviderSessionId = orderId,
                Status = PaymentSessionStatus.Created,
                CreatedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync();
        }

        using var client = _factory.CreateClient();

        var merchantId = "123123";
        var merchantSecret = "secret123";
        var amount = "150.00";
        var currency = "LKR";
        var statusCode = "2";
        var paymentId = "PH-PAYMENT-1";
        var signature = BuildPayHereSignature(merchantId, orderId, amount, currency, statusCode, merchantSecret);

        var form = new Dictionary<string, string>
        {
            ["merchant_id"] = merchantId,
            ["order_id"] = orderId,
            ["payment_id"] = paymentId,
            ["status_code"] = statusCode,
            ["payhere_amount"] = amount,
            ["payhere_currency"] = currency,
            ["md5sig"] = signature,
            ["custom_1"] = userId.ToString()
        };

        var response = await client.PostAsync("/api/webhooks/payhere", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var payment = await verifyDb.PaymentSessions.AsNoTracking().FirstAsync(p => p.ProviderSessionId == orderId);
        Assert.Equal(PaymentSessionStatus.Paid, payment.Status);
        Assert.Equal(paymentId, payment.ProviderReferenceId);

        var subscription = await verifyDb.Subscriptions.AsNoTracking().FirstAsync(s => s.SubscriptionId == subscriptionId);
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);

        var tenant = await verifyDb.Tenants.AsNoTracking().FirstAsync(t => t.TenantId == tenantId);
        Assert.Equal(TenantStatus.Active, tenant.Status);

        var user = await verifyDb.Users.AsNoTracking().FirstAsync(u => u.UserId == userId);
        Assert.Equal(UserType.TenantOperational, user.UserType);
    }

    [Fact]
    public async Task PayHere_webhook_invalid_signature_returns_bad_request()
    {
        using var client = _factory.CreateClient();

        var form = new Dictionary<string, string>
        {
            ["merchant_id"] = "123123",
            ["order_id"] = Guid.NewGuid().ToString("N"),
            ["payment_id"] = "PH-INVALID",
            ["status_code"] = "2",
            ["payhere_amount"] = "100.00",
            ["payhere_currency"] = "LKR",
            ["md5sig"] = "INVALID_SIGNATURE"
        };

        var response = await client.PostAsync("/api/webhooks/payhere", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Onboarding_checkout_returns_503_in_production_when_payhere_not_configured()
    {
        await using var factory = new ProductionUnconfiguredPaymentFactory();
        using var client = factory.CreateClient();

        Guid tenantId;
        Guid userId;
        Guid planId;
        string onboardingToken;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var onboardingTokenService = scope.ServiceProvider.GetRequiredService<IOnboardingTokenService>();

            tenantId = Guid.NewGuid();
            userId = Guid.NewGuid();
            planId = Guid.NewGuid();

            db.Tenants.Add(new Tenant
            {
                TenantId = tenantId,
                Name = "Prod Guard Tenant",
                Status = TenantStatus.Pending,
                CreatedAt = DateTime.UtcNow
            });

            db.Users.Add(new User
            {
                UserId = userId,
                Email = "prod-guard@test.local",
                FirstName = "Prod",
                LastName = "Guard",
                Country = "LK",
                Gender = "NA",
                PasswordHash = "hash",
                CreatedAt = DateTime.UtcNow,
                IsActive = true,
                Status = UserStatus.Active,
                UserType = UserType.TenantOperational
            });

            db.TenantUsers.Add(new TenantUser
            {
                TenantUserId = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = userId,
                Role = TenantUserRole.TenantAdmin,
                CreatedAt = DateTime.UtcNow
            });

            db.Plans.Add(new Plan
            {
                PlanId = planId,
                Code = "PROD-GUARD",
                Name = "Prod Guard Plan",
                PriceCents = 10000,
                Currency = "LKR",
                DurationDays = 30,
                MaxUsers = 5,
                MaxAccounts = 2,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync();

            onboardingToken = onboardingTokenService.CreateToken(userId, tenantId, "prod-guard@test.local");
        }

        var response = await client.PostAsJsonAsync("/api/onboarding/subscriptions/checkout", new OnboardingCheckoutRequest
        {
            OnboardingToken = onboardingToken,
            PlanId = planId
        });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    private static string BuildPayHereSignature(string merchantId, string orderId, string amount, string currency, string statusCode, string merchantSecret)
    {
        var secretHash = ComputeMd5Hex(merchantSecret).ToUpperInvariant();
        var signatureInput = $"{merchantId}{orderId}{amount}{currency}{statusCode}{secretHash}";
        return ComputeMd5Hex(signatureInput).ToUpperInvariant();
    }

    private static string ComputeMd5Hex(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var hash = MD5.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private sealed class ProductionUnconfiguredPaymentFactory : WebApplicationFactory<Program>
    {
        private readonly string _dbName = $"ProdGuardDb-{Guid.NewGuid()}";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                var settings = new Dictionary<string, string?>
                {
                    ["Jwt:Key"] = "test-signing-key-should-be-long-32chars",
                    ["Jwt:Issuer"] = "test-issuer",
                    ["Jwt:Audience"] = "test-audience",
                    ["Jwt:ExpiresMinutes"] = "60",
                    ["FRONTEND_BASE_URL"] = "http://localhost:4200",
                    ["PAYHERE_MERCHANT_ID"] = string.Empty,
                    ["PAYHERE_MERCHANT_SECRET"] = string.Empty,
                    ["PAYHERE_NOTIFY_URL"] = string.Empty,
                    ["PAYHERE_RETURN_URL"] = string.Empty,
                    ["PAYHERE_CANCEL_URL"] = string.Empty,
                    ["PAYMENT_ALLOW_LOCAL_BYPASS"] = "true",
                    ["BREVO_TEMPLATE_ID_MAGICLINK_VERIFY"] = "1",
                    ["BREVO_TEMPLATE_ID_MAGICLINK_INVITE"] = "4",
                    ["BREVO_TEMPLATE_ID_MAGICLINK_RESET"] = "2",
                    ["BREVO_TEMPLATE_ID_MAGICLINK_CHANGE"] = "3"
                };
                config.AddInMemoryCollection(settings);
            });

            builder.ConfigureServices(services =>
            {
                services.RemoveAll(typeof(DbContextOptions<AppDbContext>));
                services.AddDbContext<AppDbContext>(options =>
                {
                    options.UseInMemoryDatabase(_dbName);
                });
            });
        }
    }
}
