namespace finrecon360_backend.Models
{
    public class User
    {
        public Guid UserId { get; set; }
        public string Email { get; set; } = default!;
        public string? DisplayName { get; set; }
        public string? PhoneNumber { get; set; }
        public bool EmailConfirmed { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
        public string Gender { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = default!;
        public string? VerificationCode { get; set; }
        public DateTime? VerificationCodeExpiresAt { get; set; }
        public byte[]? ProfileImage { get; set; }
        public string? ProfileImageContentType { get; set; }

        public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
        public ICollection<AuthActionToken> AuthActionTokens { get; set; } = new List<AuthActionToken>();
        public ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();
    }
}
