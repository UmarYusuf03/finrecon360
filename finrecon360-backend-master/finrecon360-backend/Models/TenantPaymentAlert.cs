namespace finrecon360_backend.Models
{
    /// <summary>
    /// WHY: A missed-payment notification for system admins to review. Deliberately inert —
    /// raising this record never changes tenant/subscription state on its own. Whether to
    /// suspend, wait, or write it off stays a human decision made from the Tenants screen.
    /// </summary>
    public class TenantPaymentAlert
    {
        public Guid TenantPaymentAlertId { get; set; }
        public Guid TenantId { get; set; }
        public Guid SubscriptionId { get; set; }
        public DateTime PeriodEndAt { get; set; }
        public int DaysOverdue { get; set; }
        public PaymentAlertStatus Status { get; set; } = PaymentAlertStatus.Open;
        public DateTime CreatedAt { get; set; }
        public DateTime? AcknowledgedAt { get; set; }
        public Guid? AcknowledgedByUserId { get; set; }
        public DateTime? ResolvedAt { get; set; }
        public Guid? ResolvedByUserId { get; set; }

        public Tenant Tenant { get; set; } = default!;
        public Subscription Subscription { get; set; } = default!;
    }
}
