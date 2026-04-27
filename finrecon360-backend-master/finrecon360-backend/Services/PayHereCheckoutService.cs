using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using finrecon360_backend.Options;
using Microsoft.AspNetCore.Http;
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
            CancellationToken cancellationToken = default);

        PayHereCallbackResult ParseCallback(IFormCollection form);
        bool IsConfigured();
        string GetFallbackCheckoutUrl();
    }

    /// <summary>
    /// WHY: This implements the specific PayHere checkout flow. It securely generates 
    /// MD5 hashes required by the PayHere API to prevent tampering of amounts/currency 
    /// in transit. It also explicitly validates webhook callbacks using the merchant secret.
    /// </summary>
    public class PayHereCheckoutService : IPayHereCheckoutService
    {
        private readonly PayHereOptions _options;

        public PayHereCheckoutService(IOptions<PayHereOptions> options)
        {
            _options = options.Value;
        }

        public Task<PayHereCheckoutSession> CreateCheckoutSessionAsync(
            string name,
            long amountCents,
            Guid tenantId,
            Guid subscriptionId,
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            if (!IsConfigured())
            {
                throw new InvalidOperationException("PayHere is not configured for checkout.");
            }

            var orderId = subscriptionId.ToString("N");
            var amount = (amountCents / 100m).ToString("0.00", CultureInfo.InvariantCulture);
            var currency = string.IsNullOrWhiteSpace(_options.Currency) ? "LKR" : _options.Currency.ToUpperInvariant();

            var merchantSecretHash = ToMd5Hex(_options.MerchantSecret).ToUpperInvariant();
            var hashInput = $"{_options.MerchantId}{orderId}{amount}{currency}{merchantSecretHash}";
            var hash = ToMd5Hex(hashInput).ToUpperInvariant();

            var query = new Dictionary<string, string>
            {
                ["merchant_id"] = _options.MerchantId,
                ["return_url"] = _options.ReturnUrl,
                ["cancel_url"] = _options.CancelUrl,
                ["notify_url"] = _options.NotifyUrl,
                ["order_id"] = orderId,
                ["items"] = name,
                ["currency"] = currency,
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

            var merchantSecretHash = ToMd5Hex(_options.MerchantSecret).ToUpperInvariant();
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

        private static string BuildUrl(string baseUrl, IReadOnlyDictionary<string, string> query)
        {
            var separator = baseUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
            var pairs = query
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
                .Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}");

            return $"{baseUrl}{separator}{string.Join("&", pairs)}";
        }

        private static string ToMd5Hex(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            var hash = MD5.HashData(bytes);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}
