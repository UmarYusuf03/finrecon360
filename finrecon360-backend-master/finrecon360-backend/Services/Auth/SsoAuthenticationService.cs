using finrecon360_backend.Data;
using finrecon360_backend.Models;
using finrecon360_backend.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace finrecon360_backend.Services.Auth
{
    public record SsoSignInResult(
        bool Succeeded,
        string? Token,
        string? Email,
        string? FullName,
        bool IsNewAccount,
        string? Error)
    {
        public static SsoSignInResult Fail(string error) => new(false, null, null, null, false, error);
    }

    public interface ISsoAuthenticationService
    {
        Task<SsoSignInResult> SignInWithGoogleAsync(string idToken, CancellationToken ct = default);
    }

    /// <summary>
    /// Turns a verified Google identity into a FinRecon360 session.
    ///
    /// The external provider proves *who someone is*. It says nothing about what they may do here —
    /// roles, tenant membership and account status remain entirely ours. So once the identity is
    /// established this issues the same JWT the password login issues and enforces the same account
    /// checks, and SSO becomes just another way through the same door rather than a second door.
    /// </summary>
    public class SsoAuthenticationService : ISsoAuthenticationService
    {
        private const string GoogleProvider = "Google";

        private readonly AppDbContext _dbContext;
        private readonly IGoogleIdTokenValidator _googleValidator;
        private readonly IJwtTokenService _jwtTokenService;
        private readonly IAuditLogger _auditLogger;
        private readonly GoogleSsoOptions _options;
        private readonly ILogger<SsoAuthenticationService> _logger;

        public SsoAuthenticationService(
            AppDbContext dbContext,
            IGoogleIdTokenValidator googleValidator,
            IJwtTokenService jwtTokenService,
            IAuditLogger auditLogger,
            IOptions<GoogleSsoOptions> options,
            ILogger<SsoAuthenticationService> logger)
        {
            _dbContext = dbContext;
            _googleValidator = googleValidator;
            _jwtTokenService = jwtTokenService;
            _auditLogger = auditLogger;
            _options = options.Value;
            _logger = logger;
        }

        public async Task<SsoSignInResult> SignInWithGoogleAsync(string idToken, CancellationToken ct = default)
        {
            var validation = await _googleValidator.ValidateAsync(idToken, ct);
            if (!validation.IsValid || validation.Identity is null)
            {
                return SsoSignInResult.Fail(validation.Error ?? "Google sign-in could not be verified.");
            }

            var identity = validation.Identity;
            var normalizedEmail = identity.Email.Trim();

            // Match on the provider's subject first. It is immutable, whereas an email address can
            // be changed at Google — matching on email alone would strand the account on rename.
            var user = await _dbContext.Users.FirstOrDefaultAsync(
                u => u.ExternalProvider == GoogleProvider && u.ExternalProviderId == identity.Subject,
                ct);

            var isNewAccount = false;

            if (user is null)
            {
                user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail, ct);

                if (user is not null)
                {
                    // Linking an existing password account to Google. Safe only because Google has
                    // verified this address — the validator rejects unverified ones — so the person
                    // signing in has demonstrably proven control of the mailbox.
                    user.ExternalProvider = GoogleProvider;
                    user.ExternalProviderId = identity.Subject;
                    user.EmailConfirmed = true;
                    user.UpdatedAt = DateTime.UtcNow;

                    _logger.LogInformation("Linked existing account {Email} to Google sign-in", normalizedEmail);
                    await _auditLogger.LogAsync(user.UserId, "SsoAccountLinked", "User", user.UserId.ToString(), "provider=Google");
                }
                else
                {
                    if (!_options.AllowAutoProvisioning)
                    {
                        _logger.LogWarning("Rejected Google sign-in for unknown account {Email}: auto-provisioning disabled", normalizedEmail);
                        return SsoSignInResult.Fail("No account exists for this Google address.");
                    }

                    user = CreateUserFromGoogle(identity, normalizedEmail);
                    _dbContext.Users.Add(user);
                    isNewAccount = true;

                    _logger.LogInformation("Created new account {Email} from Google sign-in", normalizedEmail);
                    await _auditLogger.LogAsync(user.UserId, "SsoAccountCreated", "User", user.UserId.ToString(), "provider=Google");
                }
            }

            // The same gate the password login applies. A suspended or deactivated account must not
            // become reachable simply because it arrived via a different sign-in route.
            if (!user.IsActive || user.Status != UserStatus.Active)
            {
                _logger.LogWarning("Rejected Google sign-in for {Email}: account is {Status}", normalizedEmail, user.Status);
                return SsoSignInResult.Fail("This account is not active. Contact your administrator.");
            }

            // Keep the profile fresh from the provider, but never overwrite something we hold with
            // nothing — a missing Google claim should not blank out a name already on file.
            if (!string.IsNullOrWhiteSpace(identity.FirstName) && string.IsNullOrWhiteSpace(user.FirstName))
            {
                user.FirstName = identity.FirstName!;
            }

            if (!string.IsNullOrWhiteSpace(identity.LastName) && string.IsNullOrWhiteSpace(user.LastName))
            {
                user.LastName = identity.LastName!;
            }

            await _dbContext.SaveChangesAsync(ct);

            var token = _jwtTokenService.GenerateToken(user);
            var fullName = $"{user.FirstName} {user.LastName}".Trim();

            await _auditLogger.LogAsync(user.UserId, "SsoLoginSucceeded", "User", user.UserId.ToString(), "provider=Google");

            return new SsoSignInResult(
                Succeeded: true,
                Token: token,
                Email: user.Email,
                FullName: string.IsNullOrWhiteSpace(fullName) ? user.Email : fullName,
                IsNewAccount: isNewAccount,
                Error: null);
        }

        /// <summary>
        /// New SSO accounts start as GlobalPublic with no password and no elevated flags.
        /// Nothing arriving from an external provider may mint an administrator — privilege is
        /// granted inside this system, never asserted from outside it.
        /// </summary>
        private static User CreateUserFromGoogle(GoogleIdentity identity, string normalizedEmail) => new()
        {
            UserId = Guid.NewGuid(),
            Email = normalizedEmail,
            FirstName = identity.FirstName ?? string.Empty,
            LastName = identity.LastName ?? string.Empty,
            DisplayName = $"{identity.FirstName} {identity.LastName}".Trim(),
            ExternalProvider = GoogleProvider,
            ExternalProviderId = identity.Subject,
            PasswordHash = null,
            EmailConfirmed = true,
            IsActive = true,
            Status = UserStatus.Active,
            UserType = UserType.GlobalPublic,
            IsSystemAdmin = false,
            Country = string.Empty,
            Gender = string.Empty,
            CreatedAt = DateTime.UtcNow
        };
    }
}
