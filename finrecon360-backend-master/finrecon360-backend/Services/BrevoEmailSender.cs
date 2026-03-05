using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using finrecon360_backend.Options;
using Microsoft.Extensions.Options;

namespace finrecon360_backend.Services
{
    public class BrevoEmailSender : IEmailSender
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        private readonly HttpClient _httpClient;
        private readonly BrevoOptions _options;
        private readonly ILogger<BrevoEmailSender> _logger;

        public BrevoEmailSender(HttpClient httpClient, IOptions<BrevoOptions> options, ILogger<BrevoEmailSender> logger)
        {
            _httpClient = httpClient;
            _options = options.Value;
            _logger = logger;
        }

        public async Task SendTemplateAsync(string toEmail, long templateId, IDictionary<string, object> parameters, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_options.ApiKey))
            {
                throw new InvalidOperationException("Brevo API key not configured.");
            }

            var payload = new
            {
                sender = new { name = _options.SenderName, email = _options.SenderEmail },
                to = new[] { new { email = toEmail } },
                templateId = templateId,
                @params = parameters
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, "smtp/email");
            request.Headers.Add("api-key", _options.ApiKey);
            request.Content = JsonContent.Create(payload, options: JsonOptions);

            const int maxAttempts = 3;
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                using var response = await _httpClient.SendAsync(request, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var requestId = response.Headers.TryGetValues("x-request-id", out var values)
                        ? values.FirstOrDefault()
                        : null;
                    _logger.LogInformation("Brevo email sent (template {TemplateId}). RequestId={RequestId}", templateId, requestId ?? "n/a");
                    return;
                }

                if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout)
                {
                    if (attempt == maxAttempts)
                    {
                        break;
                    }

                    await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
                    continue;
                }

                var requestIdHeader = response.Headers.TryGetValues("x-request-id", out var headerValues)
                    ? headerValues.FirstOrDefault()
                    : null;
                _logger.LogWarning("Brevo email failed with status {StatusCode}. RequestId={RequestId}", (int)response.StatusCode, requestIdHeader ?? "n/a");
                response.EnsureSuccessStatusCode();
            }

            throw new InvalidOperationException("Brevo email send failed after retries.");
        }
    }
}
