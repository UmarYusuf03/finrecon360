using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using finrecon360_backend.Options;

namespace finrecon360_backend.Services.Auth
{
    /// <summary>
    /// The identity Google vouches for, after we have verified that Google really said it.
    /// </summary>
    public record GoogleIdentity(
        string Subject,
        string Email,
        bool EmailVerified,
        string? FirstName,
        string? LastName,
        string? PictureUrl);

    public record GoogleValidationResult(bool IsValid, GoogleIdentity? Identity, string? Error)
    {
        public static GoogleValidationResult Fail(string error) => new(false, null, error);
        public static GoogleValidationResult Success(GoogleIdentity identity) => new(true, identity, null);
    }

    public interface IGoogleIdTokenValidator
    {
        Task<GoogleValidationResult> ValidateAsync(string idToken, CancellationToken ct = default);
        bool IsConfigured();
    }

    /// <summary>
    /// WHY this validates rather than decodes: an ID token is just a signed JSON blob that the
    /// browser hands us. Anyone can craft one. What makes it trustworthy is the signature — signed
    /// by Google's private key, verified against Google's published public keys — plus three checks
    /// that a signature alone does not give you:
    ///
    ///   - issuer:   the token was minted by Google, not another provider.
    ///   - audience: it was minted *for this application*. Without this check a token issued to any
    ///               other Google app would be accepted here, which is a full account takeover.
    ///   - expiry:   it is still current, so a captured token cannot be replayed indefinitely.
    ///
    /// Keys are fetched from Google's OpenID discovery document and cached by ConfigurationManager,
    /// which also handles Google rotating its signing keys without us redeploying.
    /// </summary>
    public class GoogleIdTokenValidator : IGoogleIdTokenValidator
    {
        private const string GoogleDiscoveryEndpoint = "https://accounts.google.com/.well-known/openid-configuration";

        private static readonly string[] ValidIssuers =
        {
            "https://accounts.google.com",
            "accounts.google.com"
        };

        private readonly GoogleSsoOptions _options;
        private readonly ILogger<GoogleIdTokenValidator> _logger;
        private readonly IConfigurationManager<OpenIdConnectConfiguration> _configurationManager;

        public GoogleIdTokenValidator(
            IOptions<GoogleSsoOptions> options,
            ILogger<GoogleIdTokenValidator> logger)
        {
            _options = options.Value;
            _logger = logger;

            _configurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                GoogleDiscoveryEndpoint,
                new OpenIdConnectConfigurationRetriever(),
                new HttpDocumentRetriever());
        }

        public bool IsConfigured() => !string.IsNullOrWhiteSpace(_options.ClientId);

        public async Task<GoogleValidationResult> ValidateAsync(string idToken, CancellationToken ct = default)
        {
            if (!IsConfigured())
            {
                _logger.LogError("Google SSO sign-in attempted but GOOGLE_CLIENT_ID is not configured");
                return GoogleValidationResult.Fail("Google sign-in is not configured.");
            }

            if (string.IsNullOrWhiteSpace(idToken))
            {
                return GoogleValidationResult.Fail("Missing Google credential.");
            }

            OpenIdConnectConfiguration configuration;
            try
            {
                configuration = await _configurationManager.GetConfigurationAsync(ct);
            }
            catch (Exception ex)
            {
                // Google unreachable is a transient infrastructure problem, not a bad credential.
                _logger.LogError(ex, "Could not retrieve Google's OpenID configuration");
                return GoogleValidationResult.Fail("Could not reach Google to verify the sign-in. Try again.");
            }

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuers = ValidIssuers,
                ValidateAudience = true,
                ValidAudience = _options.ClientId,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKeys = configuration.SigningKeys,
                ClockSkew = TimeSpan.FromMinutes(2)
            };

            try
            {
                var handler = new JwtSecurityTokenHandler();

                // WHY this line matters: by default the handler rewrites standard OpenID claim
                // names into legacy WS-Federation URIs — "sub" becomes
                // ".../identity/claims/nameidentifier", "email" becomes ".../emailaddress".
                // Looking up "sub" then silently returns null, and a perfectly valid Google token
                // fails as if the claims were missing. Clearing the map keeps the claim names
                // exactly as Google issued them.
                // Cleared on this instance only, so the app's own JWT handling is unaffected.
                handler.InboundClaimTypeMap.Clear();

                var principal = handler.ValidateToken(idToken, validationParameters, out _);

                var subject = principal.FindFirst("sub")?.Value;
                var email = principal.FindFirst("email")?.Value;
                var emailVerifiedRaw = principal.FindFirst("email_verified")?.Value;

                if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(email))
                {
                    return GoogleValidationResult.Fail("Google did not return an account identifier and email.");
                }

                var emailVerified = bool.TryParse(emailVerifiedRaw, out var parsed) && parsed;

                // WHY unverified email is rejected outright: the email address is what we match an
                // existing account on. Accepting an unverified one would let somebody sign in as a
                // Google account carrying an address they have never proven they control.
                if (!emailVerified)
                {
                    _logger.LogWarning("Rejected Google sign-in for {Email}: email not verified with Google", email);
                    return GoogleValidationResult.Fail("Your Google email address is not verified.");
                }

                // Optional tenancy restriction: when set, only accounts in this Google Workspace
                // domain may sign in.
                if (!string.IsNullOrWhiteSpace(_options.HostedDomain))
                {
                    var hostedDomain = principal.FindFirst("hd")?.Value;
                    if (!string.Equals(hostedDomain, _options.HostedDomain, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogWarning(
                            "Rejected Google sign-in for {Email}: hosted domain '{Actual}' is not '{Expected}'",
                            email, hostedDomain ?? "(none)", _options.HostedDomain);
                        return GoogleValidationResult.Fail("This Google account is not permitted to sign in here.");
                    }
                }

                return GoogleValidationResult.Success(new GoogleIdentity(
                    Subject: subject,
                    Email: email.Trim(),
                    EmailVerified: true,
                    FirstName: principal.FindFirst("given_name")?.Value,
                    LastName: principal.FindFirst("family_name")?.Value,
                    PictureUrl: principal.FindFirst("picture")?.Value));
            }
            catch (SecurityTokenException ex)
            {
                // Covers bad signature, wrong audience, wrong issuer and expiry. Deliberately
                // reported to the caller as one generic failure — telling an attacker *which*
                // check failed helps them craft the next attempt.
                _logger.LogWarning(ex, "Google ID token failed validation");
                return GoogleValidationResult.Fail("Google sign-in could not be verified.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error validating Google ID token");
                return GoogleValidationResult.Fail("Google sign-in could not be verified.");
            }
        }
    }
}
