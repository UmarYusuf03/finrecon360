namespace finrecon360_backend.Models
{
    /// <summary>
    /// WHY: Represents the global canonical pricing tiers. It is stored in the 
    /// central control plane instead of tenant DBs because plans are platform-wide parameters 
    /// that dictate tier-limits (MaxUsers) before a tenant is legally permitted to scale.
    /// </summary>
    public class Plan
    {
        public Guid PlanId { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public long PriceCents { get; set; }
        public string Currency { get; set; } = "USD";
        public int DurationDays { get; set; }
        public int MaxUsers { get; set; }
        public int MaxAccounts { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }

        public ICollection<Subscription> Subscriptions { get; set; } = new List<Subscription>();
    }
}
