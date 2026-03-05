namespace finrecon360_backend.Models
{
    public class MagicLinkToken
    {
        public Guid MagicLinkTokenId { get; set; }
        public Guid GlobalUserId { get; set; }
        public string Purpose { get; set; } = string.Empty;
        public byte[] TokenHash { get; set; } = Array.Empty<byte>();
        public byte[] TokenSalt { get; set; } = Array.Empty<byte>();
        public DateTime ExpiresAt { get; set; }
        public DateTime? UsedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? CreatedIp { get; set; }
        public int AttemptCount { get; set; }
        public DateTime? LastAttemptAt { get; set; }

        public User GlobalUser { get; set; } = default!;
    }
}
