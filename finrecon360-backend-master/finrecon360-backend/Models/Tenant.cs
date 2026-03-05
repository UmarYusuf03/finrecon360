namespace finrecon360_backend.Models
{
    public class Tenant
    {
        public Guid TenantId { get; set; }
        public string Name { get; set; } = string.Empty;
        public TenantStatus Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ActivatedAt { get; set; }
        public string? PrimaryDomain { get; set; }
        public Guid? CurrentSubscriptionId { get; set; }

        public Subscription? CurrentSubscription { get; set; }
        public ICollection<TenantDatabase> Databases { get; set; } = new List<TenantDatabase>();
        public ICollection<TenantUser> TenantUsers { get; set; } = new List<TenantUser>();
    }
}
