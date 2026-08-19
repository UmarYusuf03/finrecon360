namespace finrecon360_backend.Models
{
    /// <summary>
    /// WHY: A single-row control-plane settings record. Kept as its own tiny table rather than
    /// a generic key-value store because there is exactly one billing knob today; a generic
    /// settings table would be speculative complexity with no second consumer yet.
    ///
    /// A payment alert is always raised the moment a subscription's period lapses (day 0) —
    /// that part is not configurable, since a tenant should always know immediately. This
    /// setting only controls the grace period before SubscriptionOverdueMonitorHostedService
    /// automatically suspends the tenant.
    /// </summary>
    public class SystemBillingSettings
    {
        public Guid SystemBillingSettingsId { get; set; }
        public int PaymentOverdueSuspensionThresholdDays { get; set; } = 7;
        public DateTime UpdatedAt { get; set; }
        public Guid? UpdatedByUserId { get; set; }
    }
}
