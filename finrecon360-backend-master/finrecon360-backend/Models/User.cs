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
        public UserStatus Status { get; set; } = UserStatus.Active;
        public UserType UserType { get; set; } = UserType.GlobalPublic;
        public bool IsSystemAdmin { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
        public string Gender { get; set; } = string.Empty;
        /// <summary>
        /// Null for users who only ever sign in through an external identity provider.
        /// "This account has no password" is a real state, not an empty string — a sentinel value
        /// here would be a hash some input could theoretically satisfy.
        /// </summary>
        public string? PasswordHash { get; set; }

        /// <summary>
        /// External identity provider for SSO accounts, e.g. "Google". Null for password accounts.
        /// </summary>
        public string? ExternalProvider { get; set; }

        /// <summary>
        /// The provider's own immutable identifier for this user — Google's "sub" claim.
        /// Matched on in preference to email, because a person can change their email address at
        /// the provider but the subject identifier stays put.
        /// </summary>
        public string? ExternalProviderId { get; set; }

        public string? VerificationCode { get; set; }
        public DateTime? VerificationCodeExpiresAt { get; set; }
        public byte[]? ProfileImage { get; set; }
        public string? ProfileImageContentType { get; set; }

        public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
        public ICollection<AuthActionToken> AuthActionTokens { get; set; } = new List<AuthActionToken>();
        public ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();
    }
}
