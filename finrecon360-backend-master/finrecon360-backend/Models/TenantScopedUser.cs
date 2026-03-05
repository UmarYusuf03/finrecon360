namespace finrecon360_backend.Models
{
    public class TenantScopedUser
    {
        public Guid TenantUserId { get; set; }
        public Guid UserId { get; set; }
        public string Email { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        public string Role { get; set; } = TenantUserRole.TenantUser.ToString();
        public string Status { get; set; } = UserStatus.Active.ToString();
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
