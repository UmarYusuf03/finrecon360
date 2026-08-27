namespace finrecon360_backend.Services
{
    public record PaymentCheckoutSession(string Provider, string SessionId, string? CustomerId, string CheckoutUrl);

    public interface IPaymentCheckoutService
    {
        Task<PaymentCheckoutSession> CreateCheckoutSessionAsync(
            string name,
            long amountCents,
            string currency,
            Guid tenantId,
            Guid subscriptionId,
            Guid userId,
            CancellationToken cancellationToken = default);

        bool IsConfigured();
        string GetFallbackCheckoutUrl();
        string GetProviderName();
    }

    /// <summary>
    /// WHY: This serves as an abstraction layer over concrete payment gateways (like PayHere).
    /// By injecting `IPaymentCheckoutService` into controllers, we can swap out or A/B test 
    /// different payment processors in the future without modifying core subscription logic.
    /// </summary>
    public class PaymentCheckoutService : IPaymentCheckoutService
    {
        private readonly IPayHereCheckoutService _payHereCheckoutService;

        public PaymentCheckoutService(
            IPayHereCheckoutService payHereCheckoutService)
        {
            _payHereCheckoutService = payHereCheckoutService;
        }

        public async Task<PaymentCheckoutSession> CreateCheckoutSessionAsync(
            string name,
            long amountCents,
            string currency,
            Guid tenantId,
            Guid subscriptionId,
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            var session = await _payHereCheckoutService.CreateCheckoutSessionAsync(
                name,
                amountCents,
                tenantId,
                subscriptionId,
                userId,
                currency,
                cancellationToken);

            // PayHere's checkout endpoint only accepts a form POST, so callers are sent through our
            // own launch page (which auto-submits the real PayHere request) rather than session.CheckoutUrl
            // directly — a GET navigation to that URL just bounces to PayHere's public homepage.
            var launchUrl = $"/api/payments/payhere/checkout/{session.OrderId}";
            return new PaymentCheckoutSession("PayHere", session.OrderId, null, launchUrl);
        }

        public bool IsConfigured()
        {
            return _payHereCheckoutService.IsConfigured();
        }

        public string GetFallbackCheckoutUrl()
        {
            return _payHereCheckoutService.GetFallbackCheckoutUrl();
        }

        public string GetProviderName()
        {
            return "PayHere";
        }
    }
}
