namespace finrecon360_backend.Models
{
    public class PermissionAction
    {
        public Guid PermissionActionId { get; set; }
        public string Code { get; set; } = default!;
        public string Name { get; set; } = default!;
        public string? Description { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
