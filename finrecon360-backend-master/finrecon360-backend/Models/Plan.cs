namespace finrecon360_backend.Models
{
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
