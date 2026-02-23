namespace finrecon360_backend.Services
{
    public record MagicLinkToken(string Token, DateTime ExpiresAt);

    public record MagicLinkConsumeResult(bool Success, Guid? UserId, string? Email);

    public interface IMagicLinkService
    {
        Task<MagicLinkToken?> CreateTokenAsync(string email, Guid? userId, string purpose, string? createdIp, CancellationToken cancellationToken = default);
        Task<MagicLinkConsumeResult> ConsumeTokenAsync(string token, string purpose, Guid? expectedUserId = null, CancellationToken cancellationToken = default);
    }
}
