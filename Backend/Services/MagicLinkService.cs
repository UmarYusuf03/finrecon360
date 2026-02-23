using System.Security.Cryptography;
using System.Text;
using finrecon360_backend.Data;
using finrecon360_backend.Models;
using finrecon360_backend.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace finrecon360_backend.Services
{
    public class MagicLinkService : IMagicLinkService
    {
        private readonly AppDbContext _dbContext;
        private readonly MagicLinkOptions _options;
        private readonly JwtSettings _jwtSettings;

        public MagicLinkService(AppDbContext dbContext, IOptions<MagicLinkOptions> options, IOptions<JwtSettings> jwtOptions)
        {
            _dbContext = dbContext;
            _options = options.Value;
            _jwtSettings = jwtOptions.Value;
        }

        public async Task<MagicLinkToken?> CreateTokenAsync(string email, Guid? userId, string purpose, string? createdIp, CancellationToken cancellationToken = default)
        {
            var normalizedEmail = NormalizeEmail(email);
            var now = DateTime.UtcNow;

            if (_options.ResendCooldownSeconds > 0)
            {
                var cutoff = now.AddSeconds(-_options.ResendCooldownSeconds);
                var recent = await _dbContext.AuthActionTokens
                    .AsNoTracking()
                    .Where(t => t.Email == normalizedEmail && t.Purpose == purpose && t.CreatedAt >= cutoff && t.ConsumedAt == null)
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

            var entity = new AuthActionToken
            {
                AuthActionTokenId = Guid.NewGuid(),
                UserId = userId,
                Email = normalizedEmail,
                Purpose = purpose,
                TokenHash = tokenHash,
                TokenSalt = tokenSalt,
                ExpiresAt = expiresAt,
                CreatedAt = now,
                CreatedIp = createdIp,
                AttemptCount = 0
            };

            _dbContext.AuthActionTokens.Add(entity);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return new MagicLinkToken(token, expiresAt);
        }

        public async Task<MagicLinkConsumeResult> ConsumeTokenAsync(string token, string purpose, Guid? expectedUserId = null, CancellationToken cancellationToken = default)
        {
            var tokenHash = ComputeHash(token);

            var record = await _dbContext.AuthActionTokens
                .FirstOrDefaultAsync(t => t.Purpose == purpose && t.TokenHash == tokenHash, cancellationToken);

            if (record == null)
            {
                return new MagicLinkConsumeResult(false, null, null);
            }

            var now = DateTime.UtcNow;
            var maxAttempts = _options.MaxAttempts <= 0 ? 5 : _options.MaxAttempts;

            if (!CryptographicOperations.FixedTimeEquals(record.TokenHash, tokenHash))
            {
                await RegisterFailedAttempt(record, now, cancellationToken);
                return new MagicLinkConsumeResult(false, record.UserId, record.Email);
            }

            if (record.ConsumedAt != null || record.ExpiresAt <= now || record.AttemptCount >= maxAttempts)
            {
                await RegisterFailedAttempt(record, now, cancellationToken);
                return new MagicLinkConsumeResult(false, record.UserId, record.Email);
            }

            if (expectedUserId.HasValue && record.UserId != expectedUserId)
            {
                await RegisterFailedAttempt(record, now, cancellationToken);
                return new MagicLinkConsumeResult(false, record.UserId, record.Email);
            }

            record.ConsumedAt = now;
            record.LastAttemptAt = now;
            await _dbContext.SaveChangesAsync(cancellationToken);

            return new MagicLinkConsumeResult(true, record.UserId, record.Email);
        }

        private async Task RegisterFailedAttempt(AuthActionToken record, DateTime now, CancellationToken cancellationToken)
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

        private static string NormalizeEmail(string email)
        {
            return email.Trim().ToLowerInvariant();
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
