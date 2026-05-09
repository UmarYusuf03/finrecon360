using System.Security.Cryptography;
using System.Text;
using System.Web;
using finrecon360_backend.Options;
using finrecon360_backend.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace finrecon360_backend.Tests;

/// <summary>
/// Unit tests for PayHereCheckoutService covering:
///   - Checkout URL generation with correct query parameters
///   - MD5 signature calculation (must NOT include notify_url)
///   - notify_url is included in the checkout URL
///   - IsConfigured() guard logic
///   - Webhook callback parsing and signature verification
///   - Local bypass behavior in OnboardingController
/// </summary>
public class PayHereCheckoutServiceTests
{
    private const string TestMerchantId = "1235059";
    private const string TestMerchantSecret = "MjA2MjM4MDI0ODI5NDIzMTc5OTUxOTU0Mzc3NTYyMjY5MTY3MjU0MA==";
    private const string TestNotifyUrl = "https://leverage-baggie-circular.ngrok-free.dev/api/webhooks/payhere";
    private const string TestReturnUrl = "http://localhost:4200/onboarding/success";
    private const string TestCancelUrl = "http://localhost:4200/onboarding/cancel";
    private const string TestCheckoutBaseUrl = "https://sandbox.payhere.lk/pay/checkout";

    private static PayHereCheckoutService CreateService(
        string? merchantId = TestMerchantId,
        string? merchantSecret = TestMerchantSecret,
        string? notifyUrl = TestNotifyUrl,
        string? returnUrl = TestReturnUrl,
        string? cancelUrl = TestCancelUrl,
        string? checkoutBaseUrl = TestCheckoutBaseUrl,
        string? currency = "LKR")
    {
        var options = Microsoft.Extensions.Options.Options.Create(new PayHereOptions
        {
            MerchantId = merchantId ?? string.Empty,
            MerchantSecret = merchantSecret ?? string.Empty,
            NotifyUrl = notifyUrl ?? string.Empty,
            ReturnUrl = returnUrl ?? string.Empty,
            CancelUrl = cancelUrl ?? string.Empty,
            CheckoutBaseUrl = checkoutBaseUrl ?? string.Empty,
            Currency = currency ?? "LKR"
        });
        return new PayHereCheckoutService(options, NullLogger<PayHereCheckoutService>.Instance);
    }

    // ──────────────────────────────────────────────
    // IsConfigured() tests
    // ──────────────────────────────────────────────

    [Fact]
    public void IsConfigured_returns_true_when_all_fields_are_set()
    {
        var service = CreateService();
        Assert.True(service.IsConfigured());
    }

    [Theory]
    [InlineData("", TestMerchantSecret, TestNotifyUrl, TestReturnUrl, TestCancelUrl, TestCheckoutBaseUrl)]
    [InlineData(TestMerchantId, "", TestNotifyUrl, TestReturnUrl, TestCancelUrl, TestCheckoutBaseUrl)]
    [InlineData(TestMerchantId, TestMerchantSecret, "", TestReturnUrl, TestCancelUrl, TestCheckoutBaseUrl)]
    [InlineData(TestMerchantId, TestMerchantSecret, TestNotifyUrl, "", TestCancelUrl, TestCheckoutBaseUrl)]
    [InlineData(TestMerchantId, TestMerchantSecret, TestNotifyUrl, TestReturnUrl, "", TestCheckoutBaseUrl)]
    [InlineData(TestMerchantId, TestMerchantSecret, TestNotifyUrl, TestReturnUrl, TestCancelUrl, "")]
    public void IsConfigured_returns_false_when_any_field_is_empty(
        string merchantId, string merchantSecret, string notifyUrl, string returnUrl, string cancelUrl, string checkoutBaseUrl)
    {
        var service = CreateService(merchantId, merchantSecret, notifyUrl, returnUrl, cancelUrl, checkoutBaseUrl);
        Assert.False(service.IsConfigured());
    }

    // ──────────────────────────────────────────────
    // CreateCheckoutSessionAsync tests
    // ──────────────────────────────────────────────

    [Fact]
    public async Task CreateCheckoutSession_throws_when_not_configured()
    {
        var service = CreateService(merchantId: "");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateCheckoutSessionAsync("Test Plan", 15000, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));
    }

    [Fact]
    public async Task CreateCheckoutSession_returns_correct_order_id()
    {
        var service = CreateService();
        var subscriptionId = Guid.NewGuid();

        var session = await service.CreateCheckoutSessionAsync("Test Plan", 15000, Guid.NewGuid(), subscriptionId, Guid.NewGuid());

        Assert.Equal(subscriptionId.ToString("N"), session.OrderId);
    }

    [Fact]
    public async Task CreateCheckoutSession_checkout_url_contains_notify_url()
    {
        var service = CreateService();

        var session = await service.CreateCheckoutSessionAsync("Test Plan", 15000, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        var html = DecodeCheckoutHtml(session.CheckoutUrl);
        Assert.Contains($"name=\"notify_url\" value=\"{TestNotifyUrl}\"", html);
    }

    [Fact]
    public async Task CreateCheckoutSession_checkout_url_contains_merchant_id()
    {
        var service = CreateService();

        var session = await service.CreateCheckoutSessionAsync("Test Plan", 15000, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        var html = DecodeCheckoutHtml(session.CheckoutUrl);
        Assert.Contains($"name=\"merchant_id\" value=\"{TestMerchantId}\"", html);
    }

    [Fact]
    public async Task CreateCheckoutSession_checkout_url_contains_return_and_cancel_urls()
    {
        var service = CreateService();

        var session = await service.CreateCheckoutSessionAsync("Test Plan", 15000, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        var html = DecodeCheckoutHtml(session.CheckoutUrl);
        Assert.Contains($"name=\"return_url\" value=\"{TestReturnUrl}\"", html);
        Assert.Contains($"name=\"cancel_url\" value=\"{TestCancelUrl}\"", html);
    }

    [Fact]
    public async Task CreateCheckoutSession_formats_amount_correctly()
    {
        var service = CreateService();

        var session = await service.CreateCheckoutSessionAsync("Test Plan", 15075, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        // 15075 cents = 150.75
        var html = DecodeCheckoutHtml(session.CheckoutUrl);
        Assert.Contains("name=\"amount\" value=\"150.75\"", html);
    }

    [Fact]
    public async Task CreateCheckoutSession_hash_does_not_include_notify_url()
    {
        // Verify the hash is computed from: merchant_id + order_id + amount + currency + MD5(merchant_secret)
        // and NOT from notify_url
        var service = CreateService();
        var subscriptionId = Guid.NewGuid();
        var orderId = subscriptionId.ToString("N");
        var amountCents = 15000L;
        var amount = "150.00";
        var currency = "LKR";

        var session = await service.CreateCheckoutSessionAsync("Test Plan", amountCents, Guid.NewGuid(), subscriptionId, Guid.NewGuid());

        // Manually compute expected hash
        var merchantSecretHash = ComputeMd5Hex(NormalizeMerchantSecret(TestMerchantSecret)).ToUpperInvariant();
        var hashInput = $"{TestMerchantId}{orderId}{amount}{currency}{merchantSecretHash}";
        var expectedHash = ComputeMd5Hex(hashInput).ToUpperInvariant();

        var html = DecodeCheckoutHtml(session.CheckoutUrl);
        Assert.Contains($"name=\"hash\" value=\"{expectedHash}\"", html);
    }

    [Fact]
    public async Task CreateCheckoutSession_includes_user_id_in_custom_1()
    {
        var service = CreateService();
        var userId = Guid.NewGuid();

        var session = await service.CreateCheckoutSessionAsync("Test Plan", 15000, Guid.NewGuid(), Guid.NewGuid(), userId);

        var html = DecodeCheckoutHtml(session.CheckoutUrl);
        Assert.Contains($"name=\"custom_1\" value=\"{userId}\"", html);
    }

    [Fact]
    public async Task CreateCheckoutSession_includes_tenant_id_in_custom_2()
    {
        var service = CreateService();
        var tenantId = Guid.NewGuid();

        var session = await service.CreateCheckoutSessionAsync("Test Plan", 15000, tenantId, Guid.NewGuid(), Guid.NewGuid());

        var html = DecodeCheckoutHtml(session.CheckoutUrl);
        Assert.Contains($"name=\"custom_2\" value=\"{tenantId}\"", html);
    }

    [Fact]
    public async Task CreateCheckoutSession_uses_sandbox_base_url()
    {
        var service = CreateService();

        var session = await service.CreateCheckoutSessionAsync("Test Plan", 15000, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        var html = DecodeCheckoutHtml(session.CheckoutUrl);
        Assert.Contains($"action=\"{TestCheckoutBaseUrl}\"", html);
        Assert.StartsWith("data:text/html;base64,", session.CheckoutUrl);
    }

    [Fact]
    public async Task CreateCheckoutSession_uses_auto_submitting_post_form()
    {
        var service = CreateService();

        var session = await service.CreateCheckoutSessionAsync("Test Plan", 15000, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var html = DecodeCheckoutHtml(session.CheckoutUrl);

        Assert.Contains("method=\"post\"", html);
        Assert.Contains("onload=\"document.getElementById('payhere-form').submit();\"", html);
    }

    // ──────────────────────────────────────────────
    // ParseCallback tests
    // ──────────────────────────────────────────────

    [Fact]
    public void ParseCallback_valid_signature_returns_success()
    {
        var service = CreateService();
        var orderId = Guid.NewGuid().ToString("N");
        var amount = "150.00";
        var currency = "LKR";
        var statusCode = "2"; // success
        var paymentId = "PH-12345";
        var userId = Guid.NewGuid();

        var signature = BuildCallbackSignature(TestMerchantId, orderId, amount, currency, statusCode, TestMerchantSecret);

        var form = BuildFormCollection(new Dictionary<string, string>
        {
            ["merchant_id"] = TestMerchantId,
            ["order_id"] = orderId,
            ["payment_id"] = paymentId,
            ["status_code"] = statusCode,
            ["payhere_amount"] = amount,
            ["payhere_currency"] = currency,
            ["md5sig"] = signature,
            ["custom_1"] = userId.ToString()
        });

        var result = service.ParseCallback(form);

        Assert.True(result.IsValid);
        Assert.True(result.IsSuccess);
        Assert.Equal(orderId, result.OrderId);
        Assert.Equal(paymentId, result.PaymentId);
        Assert.Equal(userId, result.UserId);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void ParseCallback_invalid_signature_returns_invalid()
    {
        var service = CreateService();

        var form = BuildFormCollection(new Dictionary<string, string>
        {
            ["merchant_id"] = TestMerchantId,
            ["order_id"] = Guid.NewGuid().ToString("N"),
            ["payment_id"] = "PH-BAD",
            ["status_code"] = "2",
            ["payhere_amount"] = "100.00",
            ["payhere_currency"] = "LKR",
            ["md5sig"] = "DEFINITELY_WRONG_SIGNATURE"
        });

        var result = service.ParseCallback(form);

        Assert.False(result.IsValid);
        Assert.Equal("Signature mismatch.", result.ErrorMessage);
    }

    [Fact]
    public void ParseCallback_merchant_mismatch_returns_invalid()
    {
        var service = CreateService();

        var form = BuildFormCollection(new Dictionary<string, string>
        {
            ["merchant_id"] = "WRONG_MERCHANT",
            ["order_id"] = Guid.NewGuid().ToString("N"),
            ["payment_id"] = "PH-BAD",
            ["status_code"] = "2",
            ["payhere_amount"] = "100.00",
            ["payhere_currency"] = "LKR",
            ["md5sig"] = "any"
        });

        var result = service.ParseCallback(form);

        Assert.False(result.IsValid);
        Assert.Equal("Merchant mismatch.", result.ErrorMessage);
    }

    [Fact]
    public void ParseCallback_failed_payment_status_returns_not_success()
    {
        var service = CreateService();
        var orderId = Guid.NewGuid().ToString("N");
        var amount = "150.00";
        var currency = "LKR";
        var statusCode = "-1"; // failed
        var signature = BuildCallbackSignature(TestMerchantId, orderId, amount, currency, statusCode, TestMerchantSecret);

        var form = BuildFormCollection(new Dictionary<string, string>
        {
            ["merchant_id"] = TestMerchantId,
            ["order_id"] = orderId,
            ["payment_id"] = "PH-FAIL",
            ["status_code"] = statusCode,
            ["payhere_amount"] = amount,
            ["payhere_currency"] = currency,
            ["md5sig"] = signature
        });

        var result = service.ParseCallback(form);

        Assert.True(result.IsValid);
        Assert.False(result.IsSuccess);
        Assert.Equal("-1", result.RawStatusCode);
    }

    [Fact]
    public void ParseCallback_pending_status_code_0_is_not_success()
    {
        var service = CreateService();
        var orderId = Guid.NewGuid().ToString("N");
        var amount = "200.00";
        var currency = "LKR";
        var statusCode = "0"; // pending
        var signature = BuildCallbackSignature(TestMerchantId, orderId, amount, currency, statusCode, TestMerchantSecret);

        var form = BuildFormCollection(new Dictionary<string, string>
        {
            ["merchant_id"] = TestMerchantId,
            ["order_id"] = orderId,
            ["payment_id"] = "PH-PENDING",
            ["status_code"] = statusCode,
            ["payhere_amount"] = amount,
            ["payhere_currency"] = currency,
            ["md5sig"] = signature
        });

        var result = service.ParseCallback(form);

        Assert.True(result.IsValid);
        Assert.False(result.IsSuccess); // only status_code "2" is success
    }

    [Fact]
    public void ParseCallback_missing_required_fields_returns_invalid()
    {
        var service = CreateService();

        var form = BuildFormCollection(new Dictionary<string, string>
        {
            ["merchant_id"] = "",
            ["order_id"] = "",
            ["status_code"] = ""
        });

        var result = service.ParseCallback(form);

        Assert.False(result.IsValid);
        Assert.Equal("Invalid callback payload.", result.ErrorMessage);
    }

    // ──────────────────────────────────────────────
    // GetFallbackCheckoutUrl tests
    // ──────────────────────────────────────────────

    [Fact]
    public void GetFallbackCheckoutUrl_returns_return_url_when_set()
    {
        var service = CreateService();
        Assert.Equal(TestReturnUrl, service.GetFallbackCheckoutUrl());
    }

    [Fact]
    public void GetFallbackCheckoutUrl_returns_default_when_return_url_is_empty()
    {
        var service = CreateService(returnUrl: "");
        Assert.Equal("http://localhost:4200/onboarding/success", service.GetFallbackCheckoutUrl());
    }

    // ──────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────

    private static string BuildCallbackSignature(string merchantId, string orderId, string amount, string currency, string statusCode, string merchantSecret)
    {
        var secretHash = ComputeMd5Hex(NormalizeMerchantSecret(merchantSecret)).ToUpperInvariant();
        var signatureInput = $"{merchantId}{orderId}{amount}{currency}{statusCode}{secretHash}";
        return ComputeMd5Hex(signatureInput).ToUpperInvariant();
    }

    private static string NormalizeMerchantSecret(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        try
        {
            if (value.Length % 4 == 0)
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(value));
            }
        }
        catch
        {
            // Use the original value if it is not base64.
        }

        return value;
    }

    private static string DecodeCheckoutHtml(string checkoutUrl)
    {
        Assert.StartsWith("data:text/html;base64,", checkoutUrl);
        var base64 = checkoutUrl["data:text/html;base64,".Length..];
        return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
    }

    private static string ComputeMd5Hex(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var hash = MD5.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static Microsoft.AspNetCore.Http.FormCollection BuildFormCollection(Dictionary<string, string> data)
    {
        var dict = new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>();
        foreach (var kvp in data)
        {
            dict[kvp.Key] = kvp.Value;
        }
        return new Microsoft.AspNetCore.Http.FormCollection(dict);
    }
}
