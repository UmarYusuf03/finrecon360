using finrecon360_backend.Data;
using finrecon360_backend.Models;
using finrecon360_backend.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace finrecon360_backend.Services
{
    public record OnboardingMagicLinkToken(string Token, DateTime ExpiresAt);
    public record OnboardingMagicLinkConsumeResult(bool Success, Guid? UserId);

    public interface IOnboardingMagicLinkService
    {
        Task<OnboardingMagicLinkToken?> CreateTokenAsync(Guid userId, string purpose, string? createdIp, CancellationToken cancellationToken = default);
        Task<OnboardingMagicLinkConsumeResult> ValidateTokenAsync(string token, string purpose, CancellationToken cancellationToken = default);
        Task<OnboardingMagicLinkConsumeResult> ConsumeTokenAsync(string token, string purpose, CancellationToken cancellationToken = default);
    }

    public class OnboardingMagicLinkService : IOnboardingMagicLinkService
    {
        private readonly AppDbContext _dbContext;
        private readonly MagicLinkOptions _options;
        private readonly JwtSettings _jwtSettings;

        public OnboardingMagicLinkService(AppDbContext dbContext, IOptions<MagicLinkOptions> options, IOptions<JwtSettings> jwtOptions)
        {
            _dbContext = dbContext;
            _options = options.Value;
            _jwtSettings = jwtOptions.Value;
        }

        public async Task<OnboardingMagicLinkToken?> CreateTokenAsync(Guid userId, string purpose, string? createdIp, CancellationToken cancellationToken = default)
        {
            var now = DateTime.UtcNow;

            if (_options.ResendCooldownSeconds > 0)
            {
                var cutoff = now.AddSeconds(-_options.ResendCooldownSeconds);
                var recent = await _dbContext.MagicLinkTokens
                    .AsNoTracking()
                    .Where(t => t.GlobalUserId == userId && t.Purpose == purpose && t.CreatedAt >= cutoff && t.UsedAt == null)
                    .OrderByDescending(t => t.CreatedAt)
                    .FirstOrDefaultAsync(cancellationToken);

                if (recent != null)
                {
                    return null;
                }
            }

            var tokenBytes = RandomNumberGenerator.GetBytes(32);
            var token = Base64UrlEncode(tokenBytes);
            var tokenSalt = RandomNumberGenerator.GetBytes(16);
            var tokenHash = ComputeHash(token);
            var expiresAt = now.AddMinutes(_options.ExpiresMinutes <= 0 ? 10 : _options.ExpiresMinutes);

            var entity = new finrecon360_backend.Models.MagicLinkToken
            {
                MagicLinkTokenId = Guid.NewGuid(),
                GlobalUserId = userId,
                Purpose = purpose,
                TokenHash = tokenHash,
                TokenSalt = tokenSalt,
                ExpiresAt = expiresAt,
                CreatedAt = now,
                CreatedIp = createdIp,
                AttemptCount = 0
            };

            _dbContext.MagicLinkTokens.Add(entity);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return new OnboardingMagicLinkToken(token, expiresAt);
        }

        public async Task<OnboardingMagicLinkConsumeResult> ConsumeTokenAsync(string token, string purpose, CancellationToken cancellationToken = default)
        {
            var tokenHash = ComputeHash(token);
            var record = await _dbContext.MagicLinkTokens
                .FirstOrDefaultAsync(t => t.Purpose == purpose && t.TokenHash == tokenHash, cancellationToken);

            if (record == null)
            {
                return new OnboardingMagicLinkConsumeResult(false, null);
            }

            var now = DateTime.UtcNow;
            var maxAttempts = _options.MaxAttempts <= 0 ? 5 : _options.MaxAttempts;

            if (!CryptographicOperations.FixedTimeEquals(record.TokenHash, tokenHash))
            {
                await RegisterFailedAttempt(record, now, cancellationToken);
                return new OnboardingMagicLinkConsumeResult(false, record.GlobalUserId);
            }

            if (record.UsedAt != null || record.ExpiresAt <= now || record.AttemptCount >= maxAttempts)
            {
                await RegisterFailedAttempt(record, now, cancellationToken);
                return new OnboardingMagicLinkConsumeResult(false, record.GlobalUserId);
            }

            record.UsedAt = now;
            record.LastAttemptAt = now;
            await _dbContext.SaveChangesAsync(cancellationToken);

            return new OnboardingMagicLinkConsumeResult(true, record.GlobalUserId);
        }

        public async Task<OnboardingMagicLinkConsumeResult> ValidateTokenAsync(string token, string purpose, CancellationToken cancellationToken = default)
        {
            var tokenHash = ComputeHash(token);
            var record = await _dbContext.MagicLinkTokens
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Purpose == purpose && t.TokenHash == tokenHash, cancellationToken);

            if (record == null)
            {
                return new OnboardingMagicLinkConsumeResult(false, null);
            }

            var now = DateTime.UtcNow;
            var maxAttempts = _options.MaxAttempts <= 0 ? 5 : _options.MaxAttempts;

            if (!CryptographicOperations.FixedTimeEquals(record.TokenHash, tokenHash))
            {
                return new OnboardingMagicLinkConsumeResult(false, record.GlobalUserId);
            }

            if (record.UsedAt != null || record.ExpiresAt <= now || record.AttemptCount >= maxAttempts)
            {
                return new OnboardingMagicLinkConsumeResult(false, record.GlobalUserId);
            }

            return new OnboardingMagicLinkConsumeResult(true, record.GlobalUserId);
        }

        private async Task RegisterFailedAttempt(finrecon360_backend.Models.MagicLinkToken record, DateTime now, CancellationToken cancellationToken)
        {
            record.AttemptCount += 1;
            record.LastAttemptAt = now;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        private byte[] ComputeHash(string token)
        {
            if (string.IsNullOrWhiteSpace(_jwtSettings.Key))
            {
                throw new InvalidOperationException("JWT signing key not configured.");
            }

            var keyBytes = Encoding.UTF8.GetBytes(_jwtSettings.Key);
            using var hmac = new HMACSHA256(keyBytes);
            return hmac.ComputeHash(Encoding.UTF8.GetBytes(token));
        }

        private static string Base64UrlEncode(byte[] bytes)
        {
            return Convert.ToBase64String(bytes)
                .Replace("+", "-")
                .Replace("/", "_")
                .TrimEnd('=');
        }
    }
}
