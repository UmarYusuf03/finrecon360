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
            string companyName,
            string email,
            string phone,
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
            string companyName,
            string email,
            string phone,
            CancellationToken cancellationToken = default)
        {
            var session = await _payHereCheckoutService.CreateCheckoutSessionAsync(
                name,
                amountCents,
                tenantId,
                subscriptionId,
                userId,
                companyName,
                email,
                phone,
                cancellationToken);

            return new PaymentCheckoutSession("PayHere", session.OrderId, null, session.CheckoutUrl);
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
