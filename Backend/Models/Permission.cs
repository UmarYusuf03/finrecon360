namespace finrecon360_backend.Models
{
    public class Permission
    {
        public Guid PermissionId { get; set; }
        public string Code { get; set; } = default!;
        public string Name { get; set; } = default!;
        public string? Description { get; set; }
        public string? Module { get; set; }
        public DateTime CreatedAt { get; set; }

        public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
    }
}
