namespace finrecon360_backend.Models
{
    public class UserRole
    {
        public Guid UserId { get; set; }
        public Guid RoleId { get; set; }
        public DateTime AssignedAt { get; set; }
        public Guid? AssignedByUserId { get; set; }

        public User User { get; set; } = default!;
        public Role Role { get; set; } = default!;
    }
}
