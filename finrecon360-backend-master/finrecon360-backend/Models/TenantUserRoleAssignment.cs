namespace finrecon360_backend.Models
{
    public class TenantUserRoleAssignment
    {
        public Guid UserId { get; set; }
        public Guid RoleId { get; set; }
        public DateTime AssignedAt { get; set; }

        public TenantRole Role { get; set; } = default!;
    }
}
