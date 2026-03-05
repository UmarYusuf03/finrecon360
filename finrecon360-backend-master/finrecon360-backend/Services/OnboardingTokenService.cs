using finrecon360_backend.Models;
using finrecon360_backend.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace finrecon360_backend.Services
{
    public record OnboardingTokenResult(bool Success, Guid? UserId, Guid? TenantId, string? Email, DateTime? ExpiresAtUtc);

    public interface IOnboardingTokenService
    {
        string CreateToken(Guid userId, Guid tenantId, string email);
        OnboardingTokenResult ValidateToken(string token);
    }

    public class OnboardingTokenService : IOnboardingTokenService
    {
        private readonly JwtSettings _jwtSettings;
        private readonly OnboardingTokenOptions _options;

        public OnboardingTokenService(IOptions<JwtSettings> jwtOptions, IOptions<OnboardingTokenOptions> options)
        {
            _jwtSettings = jwtOptions.Value;
            _options = options.Value;
        }

        public string CreateToken(Guid userId, Guid tenantId, string email)
        {
            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, email),
                new Claim("tenantId", tenantId.ToString())
            };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.Key));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var expires = DateTime.UtcNow.AddMinutes(_options.ExpiresMinutes <= 0 ? 20 : _options.ExpiresMinutes);

            var token = new JwtSecurityToken(
                issuer: _options.Issuer,
                audience: _options.Audience,
                claims: claims,
                expires: expires,
                signingCredentials: creds);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public OnboardingTokenResult ValidateToken(string token)
        {
            var handler = new JwtSecurityTokenHandler();
            try
            {
                var principal = handler.ValidateToken(token, new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    ValidIssuer = _options.Issuer,
                    ValidAudience = _options.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.Key)),
                    ClockSkew = TimeSpan.FromSeconds(30)
                }, out var validatedToken);

                var sub = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
                var email = principal.FindFirstValue(JwtRegisteredClaimNames.Email);
                var tenantIdValue = principal.FindFirstValue("tenantId");

                if (!Guid.TryParse(sub, out var userId) || !Guid.TryParse(tenantIdValue, out var tenantId))
                {
                    return new OnboardingTokenResult(false, null, null, null, null);
                }

                var expiresAt = validatedToken.ValidTo;
                return new OnboardingTokenResult(true, userId, tenantId, email, expiresAt);
            }
            catch
            {
                return new OnboardingTokenResult(false, null, null, null, null);
            }
        }
    }
}
