using finrecon360_backend.Data;
using finrecon360_backend.Dtos.Auth;
using finrecon360_backend.Options;
using finrecon360_backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace finrecon360_backend.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthMagicLinksController : ControllerBase
    {
        private readonly AppDbContext _dbContext;
        private readonly IMagicLinkService _magicLinkService;
        private readonly IEmailSender _emailSender;
        private readonly IAuditLogger _auditLogger;
        private readonly IUserContext _userContext;
        private readonly IPasswordHasher _passwordHasher;
        private readonly BrevoOptions _brevoOptions;
        private readonly MagicLinkOptions _magicLinkOptions;

        public AuthMagicLinksController(
            AppDbContext dbContext,
            IMagicLinkService magicLinkService,
            IEmailSender emailSender,
            IAuditLogger auditLogger,
            IUserContext userContext,
            IPasswordHasher passwordHasher,
            IOptions<BrevoOptions> brevoOptions,
            IOptions<MagicLinkOptions> magicLinkOptions)
        {
            _dbContext = dbContext;
            _magicLinkService = magicLinkService;
            _emailSender = emailSender;
            _auditLogger = auditLogger;
            _userContext = userContext;
            _passwordHasher = passwordHasher;
            _brevoOptions = brevoOptions.Value;
            _magicLinkOptions = magicLinkOptions.Value;
        }

        [HttpPost("verify-email-link")]
        [EnableRateLimiting("auth-confirm")]
        public async Task<IActionResult> VerifyEmailLink([FromBody] VerifyEmailLinkRequest request)
        {
            var consumeResult = await _magicLinkService.ConsumeTokenAsync(request.Token, MagicLinkPurpose.EmailVerify, null);
            if (!consumeResult.Success)
            {
                await _auditLogger.LogAsync(null, "MagicLinkConsumedFailed", "AuthActionToken", null, "purpose=EmailVerify");
                return BadRequest(new { message = "Invalid or expired token." });
            }

            var user = await ResolveUserAsync(consumeResult.UserId, consumeResult.Email);
            if (user == null)
            {
                return BadRequest(new { message = "Invalid or expired token." });
            }

            user.EmailConfirmed = true;
            user.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            await _auditLogger.LogAsync(user.UserId, "EmailVerified", "User", user.UserId.ToString(), null);
            await _auditLogger.LogAsync(user.UserId, "MagicLinkConsumed", "AuthActionToken", null, "purpose=EmailVerify");

            return Ok(new { message = "Email verified." });
        }

        [HttpPost("request-password-reset-link")]
        [EnableRateLimiting("auth-link")]
        public async Task<IActionResult> RequestPasswordResetLink([FromBody] RequestPasswordResetLinkRequest request)
        {
            var email = request.Email.Trim();
            var user = await _dbContext.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Email == email && u.IsActive);

            if (user != null)
            {
                var token = await _magicLinkService.CreateTokenAsync(user.Email, user.UserId, MagicLinkPurpose.PasswordReset, HttpContext.Connection.RemoteIpAddress?.ToString());
                if (token != null)
                {
                    await SendMagicLinkEmailAsync(user.Email, _brevoOptions.TemplateIdMagicLinkReset, token, MagicLinkPurpose.PasswordReset);
                }
                await _auditLogger.LogAsync(user.UserId, "MagicLinkRequested", "AuthActionToken", null, "purpose=PasswordReset");
            }

            return Ok(new { message = "If an account exists, a link was sent." });
        }

        [HttpPost("confirm-password-reset-link")]
        [EnableRateLimiting("auth-confirm")]
        public async Task<IActionResult> ConfirmPasswordResetLink([FromBody] ConfirmPasswordResetLinkRequest request)
        {
            var consumeResult = await _magicLinkService.ConsumeTokenAsync(request.Token, MagicLinkPurpose.PasswordReset, null);
            if (!consumeResult.Success)
            {
                await _auditLogger.LogAsync(null, "MagicLinkConsumedFailed", "AuthActionToken", null, "purpose=PasswordReset");
                return BadRequest(new { message = "Invalid or expired token." });
            }

            var user = await ResolveUserAsync(consumeResult.UserId, consumeResult.Email);
            if (user == null || !user.IsActive)
            {
                return BadRequest(new { message = "Invalid or expired token." });
            }

            user.PasswordHash = _passwordHasher.Hash(request.NewPassword);
            user.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            await _auditLogger.LogAsync(user.UserId, "PasswordResetCompleted", "User", user.UserId.ToString(), null);
            await _auditLogger.LogAsync(user.UserId, "MagicLinkConsumed", "AuthActionToken", null, "purpose=PasswordReset");

            return Ok(new { message = "Password reset successful." });
        }

        [HttpPost("request-change-password-link")]
        [Authorize]
        [EnableRateLimiting("auth-link")]
        public async Task<IActionResult> RequestChangePasswordLink()
        {
            if (_userContext.UserId is not { } userId)
            {
                return Unauthorized();
            }

            var user = await _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId);
            if (user == null || !user.IsActive)
            {
                return Ok(new { message = "If an account exists, a link was sent." });
            }

            var token = await _magicLinkService.CreateTokenAsync(user.Email, user.UserId, MagicLinkPurpose.ChangePassword, HttpContext.Connection.RemoteIpAddress?.ToString());
            if (token != null)
            {
                var templateId = _brevoOptions.TemplateIdMagicLinkChange ?? _brevoOptions.TemplateIdMagicLinkReset;
                await SendMagicLinkEmailAsync(user.Email, templateId, token, MagicLinkPurpose.ChangePassword);
            }

            await _auditLogger.LogAsync(user.UserId, "MagicLinkRequested", "AuthActionToken", null, "purpose=ChangePassword");
            return Ok(new { message = "If an account exists, a link was sent." });
        }

        [HttpPost("confirm-change-password-link")]
        [Authorize]
        [EnableRateLimiting("auth-confirm")]
        public async Task<IActionResult> ConfirmChangePasswordLink([FromBody] ConfirmChangePasswordLinkRequest request)
        {
            if (_userContext.UserId is not { } userId)
            {
                return Unauthorized();
            }

            var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.UserId == userId);
            if (user == null || !user.IsActive)
            {
                return BadRequest(new { message = "Invalid credentials." });
            }

            if (!_passwordHasher.Verify(request.CurrentPassword, user.PasswordHash))
            {
                return BadRequest(new { message = "Invalid credentials." });
            }

            var consumeResult = await _magicLinkService.ConsumeTokenAsync(request.Token, MagicLinkPurpose.ChangePassword, userId);
            if (!consumeResult.Success)
            {
                await _auditLogger.LogAsync(user.UserId, "MagicLinkConsumedFailed", "AuthActionToken", null, "purpose=ChangePassword");
                return BadRequest(new { message = "Invalid or expired token." });
            }

            user.PasswordHash = _passwordHasher.Hash(request.NewPassword);
            user.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            await _auditLogger.LogAsync(user.UserId, "PasswordResetCompleted", "User", user.UserId.ToString(), "purpose=ChangePassword");
            await _auditLogger.LogAsync(user.UserId, "MagicLinkConsumed", "AuthActionToken", null, "purpose=ChangePassword");

            return Ok(new { message = "Password updated." });
        }

        private async Task SendMagicLinkEmailAsync(string email, long templateId, MagicLinkToken token, string purpose)
        {
            if (string.IsNullOrWhiteSpace(_magicLinkOptions.FrontendBaseUrl))
            {
                throw new InvalidOperationException("FRONTEND_BASE_URL is not configured.");
            }

            if (templateId <= 0)
            {
                throw new InvalidOperationException("Brevo template id not configured.");
            }

            var baseUrl = _magicLinkOptions.FrontendBaseUrl.TrimEnd('/');
            var magicLink = $"{baseUrl}/auth/magic-link?purpose={purpose}&token={token.Token}";

            var parameters = new Dictionary<string, object>
            {
                ["magicLink"] = magicLink,
                ["expiresInMinutes"] = _magicLinkOptions.ExpiresMinutes
            };

            await _emailSender.SendTemplateAsync(email, templateId, parameters);
        }

        private async Task<Models.User?> ResolveUserAsync(Guid? userId, string? email)
        {
            if (userId.HasValue)
            {
                return await _dbContext.Users.FirstOrDefaultAsync(u => u.UserId == userId.Value);
            }

            if (!string.IsNullOrWhiteSpace(email))
            {
                return await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == email);
            }

            return null;
        }
    }
}
