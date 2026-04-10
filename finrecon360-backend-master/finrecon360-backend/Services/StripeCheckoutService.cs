using finrecon360_backend.Options;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;

namespace finrecon360_backend.Services
{
    public interface IStripeCheckoutService
    {
        Task<Session> CreateCheckoutSessionAsync(string name, long amountCents, string currency, Guid tenantId, Guid subscriptionId, Guid userId, CancellationToken cancellationToken = default);
        Event ParseWebhookEvent(string payload, string signatureHeader);
        bool IsConfigured();
        string GetFallbackCheckoutUrl();
    }

    public class StripeCheckoutService : IStripeCheckoutService
    {
        private readonly StripeOptions _options;

        public StripeCheckoutService(IOptions<StripeOptions> options)
        {
            _options = options.Value;
            StripeConfiguration.ApiKey = _options.ApiKey;
        }

        public async Task<Session> CreateCheckoutSessionAsync(string name, long amountCents, string currency, Guid tenantId, Guid subscriptionId, Guid userId, CancellationToken cancellationToken = default)
        {
            if (!IsConfigured())
            {
                throw new InvalidOperationException("Stripe is not configured for checkout.");
            }

            var options = new SessionCreateOptions
            {
                Mode = "payment",
                SuccessUrl = _options.SuccessUrl,
                CancelUrl = _options.CancelUrl,
                ClientReferenceId = subscriptionId.ToString(),
                LineItems = new List<SessionLineItemOptions>
                {
                    new SessionLineItemOptions
                    {
                        Quantity = 1,
                        PriceData = new SessionLineItemPriceDataOptions
                        {
                            Currency = currency.ToLowerInvariant(),
                            UnitAmount = amountCents,
                            ProductData = new SessionLineItemPriceDataProductDataOptions
                            {
                                Name = name
                            }
                        }
                    }
                },
                Metadata = new Dictionary<string, string>
                {
                    ["tenantId"] = tenantId.ToString(),
                    ["subscriptionId"] = subscriptionId.ToString(),
                    ["userId"] = userId.ToString()
                }
            };

            var service = new SessionService();
            return await service.CreateAsync(options, cancellationToken: cancellationToken);
        }

        public bool IsConfigured()
        {
            if (string.IsNullOrWhiteSpace(_options.ApiKey))
            {
                return false;
            }

            // Stripe secret keys are expected to start with sk_.
            return _options.ApiKey.StartsWith("sk_", StringComparison.OrdinalIgnoreCase);
        }

        public string GetFallbackCheckoutUrl()
        {
            if (!string.IsNullOrWhiteSpace(_options.SuccessUrl))
            {
                return _options.SuccessUrl;
            }

            return "http://localhost:4200/onboarding/success";
        }

        public Event ParseWebhookEvent(string payload, string signatureHeader)
        {
            if (string.IsNullOrWhiteSpace(_options.WebhookSecret))
            {
                throw new InvalidOperationException("Stripe webhook secret not configured.");
            }

            return EventUtility.ConstructEvent(payload, signatureHeader, _options.WebhookSecret);
        }
    }
}
