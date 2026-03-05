namespace finrecon360_backend.Models
{
    public class Subscription
    {
        public Guid SubscriptionId { get; set; }
        public Guid TenantId { get; set; }
        public Guid PlanId { get; set; }
        public SubscriptionStatus Status { get; set; }
        public DateTime? CurrentPeriodStart { get; set; }
        public DateTime? CurrentPeriodEnd { get; set; }
        public DateTime CreatedAt { get; set; }

        public Tenant Tenant { get; set; } = default!;
        public Plan Plan { get; set; } = default!;
        public ICollection<PaymentSession> PaymentSessions { get; set; } = new List<PaymentSession>();
    }
}
