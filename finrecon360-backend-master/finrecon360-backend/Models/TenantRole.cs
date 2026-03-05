namespace finrecon360_backend.Models
{
    public class TenantRole
    {
        public Guid RoleId { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool IsSystem { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }

        public ICollection<TenantRolePermission> RolePermissions { get; set; } = new List<TenantRolePermission>();
        public ICollection<TenantUserRoleAssignment> UserRoles { get; set; } = new List<TenantUserRoleAssignment>();
    }
}
