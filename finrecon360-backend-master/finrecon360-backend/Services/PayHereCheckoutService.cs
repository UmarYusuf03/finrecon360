using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using finrecon360_backend.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
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
            string companyName,
            string email,
            string phone,
            CancellationToken cancellationToken = default);

        PayHereCallbackResult ParseCallback(IFormCollection form);
        bool IsConfigured();
        string GetFallbackCheckoutUrl();
    }

    /// <summary>
    /// WHY: This implements the specific PayHere checkout flow. It securely generates 
    /// MD5 hashes required by the PayHere API to prevent tampering of amounts/currency 
    /// in transit. It also explicitly validates webhook callbacks using the merchant secret.
    /// Isolating payment logic here allows swapping to a different gateway (Stripe, etc.) without
    /// touching controllers or core subscription logic—just replace the service implementation.
    /// </summary>
    public class PayHereCheckoutService : IPayHereCheckoutService
    {
        private readonly PayHereOptions _options;
        private readonly ILogger<PayHereCheckoutService> _logger;
        private readonly string _decodedMerchantSecret;

        public PayHereCheckoutService(IOptions<PayHereOptions> options, ILogger<PayHereCheckoutService> logger)
        {
            _options = options.Value;
            _logger = logger;
            
            // Try to decode the merchant secret if it appears to be Base64 encoded
            _decodedMerchantSecret = TryDecodeBase64(_options.MerchantSecret) ?? _options.MerchantSecret;
            
            if (_decodedMerchantSecret != _options.MerchantSecret)
            {
                _logger.LogDebug("Merchant secret was Base64 decoded from length {OriginalLength} to {DecodedLength}", 
                    _options.MerchantSecret.Length, _decodedMerchantSecret.Length);
            }
        }

        public Task<PayHereCheckoutSession> CreateCheckoutSessionAsync(
            string name,
            long amountCents,
            Guid tenantId,
            Guid subscriptionId,
            Guid userId,
            string companyName,
            string email,
            string phone,
            CancellationToken cancellationToken = default)
        {
            if (!IsConfigured())
            {
                _logger.LogError("PayHere checkout request denied: service not configured. MerchantId={MerchantId}, BaseUrl={BaseUrl}", 
                    string.IsNullOrEmpty(_options.MerchantId) ? "MISSING" : _options.MerchantId,
                    _options.CheckoutBaseUrl);
                throw new InvalidOperationException("PayHere is not configured for checkout.");
            }

            var orderId = subscriptionId.ToString("N");
            var amount = (amountCents / 100m).ToString("0.00", CultureInfo.InvariantCulture);
            var currency = string.IsNullOrWhiteSpace(_options.Currency) ? "LKR" : _options.Currency.ToUpperInvariant();

            var merchantSecretHash = ToMd5Hex(_decodedMerchantSecret).ToUpperInvariant();
            var hashInput = $"{_options.MerchantId}{orderId}{amount}{currency}{merchantSecretHash}";
            var hash = ToMd5Hex(hashInput).ToUpperInvariant();

            _logger.LogDebug("PayHere checkout hash calculation: MerchantId={MerchantId}, OrderId={OrderId}, Amount={Amount}, Currency={Currency}, Hash={Hash}", 
                _options.MerchantId, orderId, amount, currency, hash);

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
                ["first_name"] = companyName,
                ["last_name"] = "finrecon",
                ["email"] = email,
                ["phone"] = phone,
                ["address"] = "N/A",
                ["city"] = "Colombo",
                ["country"] = "Sri Lanka",
                ["custom_1"] = userId.ToString(),
                ["custom_2"] = tenantId.ToString(),
                ["hash"] = hash
            };

            var checkoutUrl = BuildAutoSubmittingFormDataUrl(query);
            
            _logger.LogInformation("PayHere checkout handoff created: OrderId={OrderId}, Amount={Amount} {Currency}, UrlType={UrlType}", 
                orderId, amount, currency, checkoutUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ? "data-url" : "url");
            System.Console.WriteLine($"[PayHere Debug] Checkout handoff created for order {orderId}");

            return Task.FromResult(new PayHereCheckoutSession(orderId, checkoutUrl));
        }

        public PayHereCallbackResult ParseCallback(IFormCollection form)
        {
            if (!IsConfigured())
            {
                _logger.LogError("PayHere callback received but service not configured");
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

            _logger.LogDebug("PayHere callback received: MerchantId={MerchantId}, OrderId={OrderId}, PaymentId={PaymentId}, StatusCode={StatusCode}, Amount={Amount}, Currency={Currency}, Signature={Signature}", 
                merchantId, orderId, paymentId, statusCode, amount, currency, signature);

            if (string.IsNullOrWhiteSpace(merchantId) || string.IsNullOrWhiteSpace(orderId) || string.IsNullOrWhiteSpace(statusCode))
            {
                _logger.LogWarning("PayHere callback validation failed: invalid payload (missing required fields)");
                return new PayHereCallbackResult(false, false, orderId, paymentId, null, statusCode, "Invalid callback payload.");
            }

            if (!string.Equals(merchantId, _options.MerchantId, StringComparison.Ordinal))
            {
                _logger.LogWarning("PayHere callback rejected: merchant mismatch. Expected={ExpectedMerchantId}, Received={ReceivedMerchantId}", 
                    _options.MerchantId, merchantId);
                return new PayHereCallbackResult(false, false, orderId, paymentId, null, statusCode, "Merchant mismatch.");
            }

            var merchantSecretHash = ToMd5Hex(_decodedMerchantSecret).ToUpperInvariant();
            var localHashInput = $"{merchantId}{orderId}{amount}{currency}{statusCode}{merchantSecretHash}";
            var localSignature = ToMd5Hex(localHashInput).ToUpperInvariant();

            _logger.LogDebug("PayHere signature validation: ExpectedSignature={ExpectedSignature}, ReceivedSignature={ReceivedSignature}, HashInput={HashInput}", 
                localSignature, signature, localHashInput);

            if (!string.Equals(signature, localSignature, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("PayHere callback rejected: signature mismatch. Expected={ExpectedSignature}, Received={ReceivedSignature}", 
                    localSignature, signature);
                return new PayHereCallbackResult(false, false, orderId, paymentId, null, statusCode, "Signature mismatch.");
            }

            Guid? userId = null;
            if (Guid.TryParse(userIdValue, out var parsedUserId))
            {
                userId = parsedUserId;
            }

            var isSuccess = string.Equals(statusCode, "2", StringComparison.Ordinal);
            _logger.LogInformation("PayHere callback validated successfully: OrderId={OrderId}, PaymentId={PaymentId}, StatusCode={StatusCode}, IsSuccess={IsSuccess}", 
                orderId, paymentId, statusCode, isSuccess);
            
            return new PayHereCallbackResult(true, isSuccess, orderId, paymentId, userId, statusCode, null);
        }

        public bool IsConfigured()
        {
            var isMerchantIdSet = !string.IsNullOrWhiteSpace(_options.MerchantId);
            var isMerchantSecretSet = !string.IsNullOrWhiteSpace(_options.MerchantSecret);
            var isNotifyUrlSet = !string.IsNullOrWhiteSpace(_options.NotifyUrl);
            var isReturnUrlSet = !string.IsNullOrWhiteSpace(_options.ReturnUrl);
            var isCancelUrlSet = !string.IsNullOrWhiteSpace(_options.CancelUrl);
            var isBaseUrlSet = !string.IsNullOrWhiteSpace(_options.CheckoutBaseUrl);

            var isConfigured = isMerchantIdSet && isMerchantSecretSet && isNotifyUrlSet && isReturnUrlSet && isCancelUrlSet && isBaseUrlSet;
            
            if (!isConfigured)
            {
                _logger.LogWarning(
                    "PayHere configuration incomplete: MerchantId={MerchantId}, Secret={Secret}, ReturnUrl={ReturnUrl}, CancelUrl={CancelUrl}, NotifyUrl={NotifyUrl}, BaseUrl={BaseUrl}",
                    isMerchantIdSet ? "✓" : "✗",
                    isMerchantSecretSet ? "✓" : "✗",
                    isReturnUrlSet ? "✓" : "✗",
                    isCancelUrlSet ? "✓" : "✗",
                    isNotifyUrlSet ? "✓" : "✗",
                    isBaseUrlSet ? "✓" : "✗");
            }
            
            return isConfigured;
        }

        public string GetFallbackCheckoutUrl()
        {
            if (!string.IsNullOrWhiteSpace(_options.ReturnUrl))
            {
                return _options.ReturnUrl;
            }

            return "http://localhost:4200/onboarding/success";
        }

        private string BuildAutoSubmittingFormDataUrl(IReadOnlyDictionary<string, string> query)
        {
            var formFields = string.Join(Environment.NewLine, query
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
                .Select(kv => $"                <input type=\"hidden\" name=\"{WebUtility.HtmlEncode(kv.Key)}\" value=\"{WebUtility.HtmlEncode(kv.Value)}\" />"));

            var html = new StringBuilder()
                .AppendLine("<!DOCTYPE html>")
                .AppendLine("<html lang=\"en\">")
                .AppendLine("<head>")
                .AppendLine("    <meta charset=\"utf-8\" />")
                .AppendLine("    <meta http-equiv=\"X-UA-Compatible\" content=\"IE=edge\" />")
                .AppendLine("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />")
                .AppendLine("    <title>Redirecting to PayHere</title>")
                .AppendLine("    <style>")
                .AppendLine("        body { font-family: system-ui, sans-serif; display: grid; place-items: center; min-height: 100vh; margin: 0; }")
                .AppendLine("        .card { text-align: center; padding: 2rem; border: 1px solid #ddd; border-radius: 12px; max-width: 420px; }")
                .AppendLine("    </style>")
                .AppendLine("</head>")
                .AppendLine("<body onload=\"document.getElementById('payhere-form').submit();\">")
                .AppendLine("    <div class=\"card\">")
                .AppendLine("        <h1>Redirecting to PayHere...</h1>")
                .AppendLine("        <p>If you are not redirected automatically, click continue.</p>")
                .AppendLine($"        <form id=\"payhere-form\" method=\"post\" action=\"{WebUtility.HtmlEncode(_options.CheckoutBaseUrl)}\">")
                .AppendLine(formFields)
                .AppendLine("            <noscript>")
                .AppendLine("                <button type=\"submit\">Continue to payment</button>")
                .AppendLine("            </noscript>")
                .AppendLine("        </form>")
                .AppendLine("    </div>")
                .AppendLine("</body>")
                .AppendLine("</html>")
                .ToString();

            var htmlBytes = Encoding.UTF8.GetBytes(html);
            return $"data:text/html;base64,{Convert.ToBase64String(htmlBytes)}";
        }

        /// <summary>
        /// Try to decode a string from Base64. Returns null if decoding fails or the string doesn't appear to be Base64.
        /// </summary>
        private static string? TryDecodeBase64(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            try
            {
                // Base64 strings typically have lengths that are multiples of 4
                if (value.Length % 4 != 0)
                    return null;

                var decodedBytes = Convert.FromBase64String(value);
                var decoded = Encoding.UTF8.GetString(decodedBytes);
                
                // If we get here, decoding succeeded
                return decoded;
            }
            catch
            {
                // If decoding fails, return null and use the original value
                return null;
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
