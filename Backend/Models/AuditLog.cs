namespace finrecon360_backend.Models
{
    public class AuditLog
    {
        public Guid AuditLogId { get; set; }
        public Guid? UserId { get; set; }
        public string Action { get; set; } = default!;
        public string? Entity { get; set; }
        public string? EntityId { get; set; }
        public string? Metadata { get; set; }
        public DateTime CreatedAt { get; set; }

        public User? User { get; set; }
    }
}
