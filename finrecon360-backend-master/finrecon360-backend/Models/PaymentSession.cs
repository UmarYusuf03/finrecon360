namespace finrecon360_backend.Models
{
    public class PaymentSession
    {
        public Guid PaymentSessionId { get; set; }
        public Guid TenantId { get; set; }
        public Guid SubscriptionId { get; set; }
        public string ProviderSessionId { get; set; } = string.Empty;
        public string? ProviderReferenceId { get; set; }
        public PaymentSessionStatus Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? PaidAt { get; set; }

        public Tenant Tenant { get; set; } = default!;
        public Subscription Subscription { get; set; } = default!;
    }
}
