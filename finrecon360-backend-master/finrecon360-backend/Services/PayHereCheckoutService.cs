using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using finrecon360_backend.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace finrecon360_backend.Services
{
    public record PayHereCheckoutSession(string OrderId, string CheckoutUrl);

    public record PayHereCallbackResult(
        bool IsValid,
        bool IsSuccess,
        string OrderId,
        string? PaymentId,
        Guid? UserId,
        string RawStatusCode,
        string? ErrorMessage);

    public interface IPayHereCheckoutService
    {
        Task<PayHereCheckoutSession> CreateCheckoutSessionAsync(
            string name,
            long amountCents,
            Guid tenantId,
            Guid subscriptionId,
            Guid userId,
            string? currency = null,
            CancellationToken cancellationToken = default);

        PayHereCallbackResult ParseCallback(IFormCollection form);
        bool IsConfigured();
        string GetFallbackCheckoutUrl();
        bool TryGetCheckoutLaunchHtml(string orderId, out string? launchHtml);
    }

    /// <summary>
    /// WHY: This implements the specific PayHere checkout flow. It securely generates 
    /// MD5 hashes required by the PayHere API to prevent tampering of amounts/currency 
    /// in transit. It also explicitly validates webhook callbacks using the merchant secret.
    /// </summary>
    public class PayHereCheckoutService : IPayHereCheckoutService
    {
        // PayHere's /pay/checkout endpoint only reads form-POST fields — a GET query string is
        // invisible to it and falls through to the public payhere.lk homepage. Browsers can't be
        // redirected straight there with a POST body, so CreateCheckoutSessionAsync's field set is
        // cached here and replayed by TryGetCheckoutLaunchHtml as an auto-submitting HTML form.
        private static readonly TimeSpan LaunchCacheDuration = TimeSpan.FromMinutes(20);

        private readonly PayHereOptions _options;
        private readonly IMemoryCache _cache;

        public PayHereCheckoutService(IOptions<PayHereOptions> options, IMemoryCache cache)
        {
            _options = options.Value;
            _cache = cache;
        }

        public Task<PayHereCheckoutSession> CreateCheckoutSessionAsync(
            string name,
            long amountCents,
            Guid tenantId,
            Guid subscriptionId,
            Guid userId,
            string? currency = null,
            CancellationToken cancellationToken = default)
        {
            if (!IsConfigured())
            {
                throw new InvalidOperationException("PayHere is not configured for checkout.");
            }

            var orderId = subscriptionId.ToString("N");
            var amount = (amountCents / 100m).ToString("0.00", CultureInfo.InvariantCulture);
            var resolvedCurrency = !string.IsNullOrWhiteSpace(currency)
                ? currency.ToUpperInvariant()
                : (string.IsNullOrWhiteSpace(_options.Currency) ? "LKR" : _options.Currency.ToUpperInvariant());

            var merchantSecretHash = ToMd5Hex(ResolveMerchantSecret()).ToUpperInvariant();
            var hashInput = $"{_options.MerchantId}{orderId}{amount}{resolvedCurrency}{merchantSecretHash}";
            var hash = ToMd5Hex(hashInput).ToUpperInvariant();

            var query = new Dictionary<string, string>
            {
                ["merchant_id"] = _options.MerchantId,
                ["return_url"] = _options.ReturnUrl,
                ["cancel_url"] = _options.CancelUrl,
                ["notify_url"] = _options.NotifyUrl,
                ["order_id"] = orderId,
                ["items"] = name,
                ["currency"] = resolvedCurrency,
                ["amount"] = amount,
                ["first_name"] = "Tenant",
                ["last_name"] = "Admin",
                ["email"] = "no-reply@finrecon.local",
                ["phone"] = "0000000000",
                ["address"] = "N/A",
                ["city"] = "Colombo",
                ["country"] = "Sri Lanka",
                ["custom_1"] = userId.ToString(),
                ["custom_2"] = tenantId.ToString(),
                ["hash"] = hash
            };

            _cache.Set(LaunchCacheKey(orderId), query, LaunchCacheDuration);

            var checkoutUrl = BuildUrl(_options.CheckoutBaseUrl, query);
            return Task.FromResult(new PayHereCheckoutSession(orderId, checkoutUrl));
        }

        public PayHereCallbackResult ParseCallback(IFormCollection form)
        {
            if (!IsConfigured())
            {
                return new PayHereCallbackResult(false, false, string.Empty, null, null, string.Empty, "PayHere is not configured.");
            }

            var merchantId = form["merchant_id"].ToString();
            var orderId = form["order_id"].ToString();
            var paymentId = form["payment_id"].ToString();
            var statusCode = form["status_code"].ToString();
            var amount = form["payhere_amount"].ToString();
            var currency = form["payhere_currency"].ToString();
            var signature = form["md5sig"].ToString();
            var userIdValue = form["custom_1"].ToString();

            if (string.IsNullOrWhiteSpace(merchantId) || string.IsNullOrWhiteSpace(orderId) || string.IsNullOrWhiteSpace(statusCode))
            {
                return new PayHereCallbackResult(false, false, orderId, paymentId, null, statusCode, "Invalid callback payload.");
            }

            if (!string.Equals(merchantId, _options.MerchantId, StringComparison.Ordinal))
            {
                return new PayHereCallbackResult(false, false, orderId, paymentId, null, statusCode, "Merchant mismatch.");
            }

            var merchantSecretHash = ToMd5Hex(ResolveMerchantSecret()).ToUpperInvariant();
            var localHashInput = $"{merchantId}{orderId}{amount}{currency}{statusCode}{merchantSecretHash}";
            var localSignature = ToMd5Hex(localHashInput).ToUpperInvariant();

            if (!string.Equals(signature, localSignature, StringComparison.OrdinalIgnoreCase))
            {
                return new PayHereCallbackResult(false, false, orderId, paymentId, null, statusCode, "Signature mismatch.");
            }

            Guid? userId = null;
            if (Guid.TryParse(userIdValue, out var parsedUserId))
            {
                userId = parsedUserId;
            }

            var isSuccess = string.Equals(statusCode, "2", StringComparison.Ordinal);
            return new PayHereCallbackResult(true, isSuccess, orderId, paymentId, userId, statusCode, null);
        }

        public bool IsConfigured()
        {
            return !string.IsNullOrWhiteSpace(_options.MerchantId)
                && !string.IsNullOrWhiteSpace(_options.MerchantSecret)
                && !string.IsNullOrWhiteSpace(_options.NotifyUrl)
                && !string.IsNullOrWhiteSpace(_options.ReturnUrl)
                && !string.IsNullOrWhiteSpace(_options.CancelUrl)
                && !string.IsNullOrWhiteSpace(_options.CheckoutBaseUrl);
        }

        public string GetFallbackCheckoutUrl()
        {
            if (!string.IsNullOrWhiteSpace(_options.ReturnUrl))
            {
                return _options.ReturnUrl;
            }

            return "http://localhost:4200/onboarding/success";
        }

        public bool TryGetCheckoutLaunchHtml(string orderId, out string? launchHtml)
        {
            launchHtml = null;

            if (!_cache.TryGetValue(LaunchCacheKey(orderId), out Dictionary<string, string>? fields) || fields == null)
            {
                return false;
            }

            var inputs = string.Join(
                "\n",
                fields
                    .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
                    .Select(kv => $"<input type=\"hidden\" name=\"{WebUtility.HtmlEncode(kv.Key)}\" value=\"{WebUtility.HtmlEncode(kv.Value)}\">"));

            launchHtml = $"""
                <!DOCTYPE html>
                <html>
                <head><meta charset="utf-8"><title>Redirecting to PayHere...</title></head>
                <body onload="document.forms[0].submit()">
                    <form method="POST" action="{WebUtility.HtmlEncode(_options.CheckoutBaseUrl)}">
                        {inputs}
                        <noscript><button type="submit">Continue to PayHere</button></noscript>
                    </form>
                    <p>Redirecting to PayHere&hellip;</p>
                </body>
                </html>
                """;

            return true;
        }

        private static string LaunchCacheKey(string orderId) => $"payhere:checkout-launch:{orderId}";

        private static string BuildUrl(string baseUrl, IReadOnlyDictionary<string, string> query)
        {
            var separator = baseUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
            var pairs = query
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
                .Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}");

            return $"{baseUrl}{separator}{string.Join("&", pairs)}";
        }

        private string ResolveMerchantSecret()
        {
            if (!string.Equals(_options.MerchantSecretMode, "Base64", StringComparison.OrdinalIgnoreCase))
            {
                return _options.MerchantSecret;
            }

            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(_options.MerchantSecret));
            }
            catch (FormatException ex)
            {
                throw new InvalidOperationException(
                    "PAYHERE_MERCHANT_SECRET_MODE is 'Base64' but PAYHERE_MERCHANT_SECRET is not valid base64.", ex);
            }
        }

        private static string ToMd5Hex(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            var hash = MD5.HashData(bytes);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}
