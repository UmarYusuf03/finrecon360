namespace finrecon360_backend.Models
{
    public class AuthActionToken
    {
        public Guid AuthActionTokenId { get; set; }
        public Guid? UserId { get; set; }
        public string Email { get; set; } = default!;
        public string Purpose { get; set; } = default!;
        public byte[] TokenHash { get; set; } = default!;
        public byte[] TokenSalt { get; set; } = default!;
        public DateTime ExpiresAt { get; set; }
        public DateTime? ConsumedAt { get; set; }
        public int AttemptCount { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? CreatedIp { get; set; }
        public DateTime? LastAttemptAt { get; set; }

        public User? User { get; set; }
    }
}
