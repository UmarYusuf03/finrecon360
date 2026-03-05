namespace finrecon360_backend.Models
{
    public class TenantUser
    {
        public Guid TenantUserId { get; set; }
        public Guid TenantId { get; set; }
        public Guid UserId { get; set; }
        public TenantUserRole Role { get; set; } = TenantUserRole.TenantUser;
        public DateTime CreatedAt { get; set; }

        public Tenant Tenant { get; set; } = default!;
        public User User { get; set; } = default!;
    }
}
