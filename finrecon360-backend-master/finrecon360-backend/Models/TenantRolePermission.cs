namespace finrecon360_backend.Models
{
    public class TenantRolePermission
    {
        public Guid RoleId { get; set; }
        public Guid PermissionId { get; set; }
        public DateTime GrantedAt { get; set; }

        public TenantRole Role { get; set; } = default!;
        public TenantPermission Permission { get; set; } = default!;
    }
}
