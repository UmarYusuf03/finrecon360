using finrecon360_backend.Data;
using finrecon360_backend.Models;
using finrecon360_backend.Options;
using finrecon360_backend.Services;
using finrecon360_backend.Services.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace finrecon360_backend.Tests;

/// <summary>
/// Covers the account-resolution rules for Google sign-in.
///
/// These tests deliberately target the security decisions rather than the happy path: an
/// external provider establishes who someone is, but every rule about what that entitles them
/// to lives here, and a regression in any of them is a way into the system.
/// </summary>
public class SsoAuthenticationServiceTests
{
    private const string ValidGoogleSubject = "google-subject-12345";
    private const string ValidEmail = "person@example.com";

    private static AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"SsoAuth-{Guid.NewGuid()}")
            .Options);

    private static SsoAuthenticationService CreateService(
        AppDbContext db,
        GoogleValidationResult validationResult,
        bool allowAutoProvisioning = true)
    {
        // Fully qualified: the project's own finrecon360_backend.Options namespace shadows
        // Microsoft.Extensions.Options.Options here.
        var options = Microsoft.Extensions.Options.Options.Create(new GoogleSsoOptions
        {
            ClientId = "test-client-id.apps.googleusercontent.com",
            AllowAutoProvisioning = allowAutoProvisioning
        });

        return new SsoAuthenticationService(
            db,
            new StubGoogleValidator(validationResult),
            new StubJwtTokenService(),
            new NoOpAuditLogger(),
            options,
            NullLogger<SsoAuthenticationService>.Instance);
    }

    private static GoogleValidationResult ValidGoogleIdentity(
        string subject = ValidGoogleSubject,
        string email = ValidEmail) =>
        GoogleValidationResult.Success(new GoogleIdentity(
            Subject: subject,
            Email: email,
            EmailVerified: true,
            FirstName: "Test",
            LastName: "Person",
            PictureUrl: null));

    [Fact]
    public async Task SignIn_creates_a_new_account_when_the_email_is_unknown()
    {
        using var db = CreateDb();
        var service = CreateService(db, ValidGoogleIdentity());

        var result = await service.SignInWithGoogleAsync("any-token");

        Assert.True(result.Succeeded);
        Assert.True(result.IsNewAccount);
        Assert.Equal(ValidEmail, result.Email);
        Assert.False(string.IsNullOrWhiteSpace(result.Token));

        var created = await db.Users.SingleAsync();
        Assert.Equal("Google", created.ExternalProvider);
        Assert.Equal(ValidGoogleSubject, created.ExternalProviderId);
        Assert.True(created.EmailConfirmed);
    }

    /// <summary>
    /// A provider can tell us who someone is. It must never be able to tell us they are an
    /// administrator — privilege is granted inside this system, never asserted from outside it.
    /// </summary>
    [Fact]
    public async Task SignIn_never_provisions_an_account_with_elevated_privileges()
    {
        using var db = CreateDb();
        var service = CreateService(db, ValidGoogleIdentity());

        await service.SignInWithGoogleAsync("any-token");

        var created = await db.Users.SingleAsync();
        Assert.False(created.IsSystemAdmin);
        Assert.Equal(UserType.GlobalPublic, created.UserType);
        Assert.Null(created.PasswordHash);
    }

    [Fact]
    public async Task SignIn_links_an_existing_password_account_on_first_google_use()
    {
        using var db = CreateDb();
        db.Users.Add(new User
        {
            UserId = Guid.NewGuid(),
            Email = ValidEmail,
            PasswordHash = "existing-hash",
            FirstName = "Existing",
            LastName = "User",
            IsActive = true,
            Status = UserStatus.Active,
            UserType = UserType.TenantOperational,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var service = CreateService(db, ValidGoogleIdentity());
        var result = await service.SignInWithGoogleAsync("any-token");

        Assert.True(result.Succeeded);
        Assert.False(result.IsNewAccount);

        // One account, not two: the same person must not end up with a duplicate record
        // simply because they switched sign-in method.
        var user = await db.Users.SingleAsync();
        Assert.Equal("Google", user.ExternalProvider);
        Assert.Equal(ValidGoogleSubject, user.ExternalProviderId);
        Assert.Equal("existing-hash", user.PasswordHash); // password sign-in still works
        Assert.Equal(UserType.TenantOperational, user.UserType); // existing role untouched
    }

    /// <summary>
    /// Google's subject id is immutable; an email address is not. Matching on the subject means
    /// an account survives the person renaming their email at the provider.
    /// </summary>
    [Fact]
    public async Task SignIn_matches_on_provider_subject_even_when_the_email_changed()
    {
        using var db = CreateDb();
        db.Users.Add(new User
        {
            UserId = Guid.NewGuid(),
            Email = "old-address@example.com",
            ExternalProvider = "Google",
            ExternalProviderId = ValidGoogleSubject,
            FirstName = "Existing",
            LastName = "User",
            IsActive = true,
            Status = UserStatus.Active,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var service = CreateService(db, ValidGoogleIdentity(email: "new-address@example.com"));
        var result = await service.SignInWithGoogleAsync("any-token");

        Assert.True(result.Succeeded);
        Assert.False(result.IsNewAccount);
        Assert.Single(await db.Users.ToListAsync());
    }

    [Fact]
    public async Task SignIn_is_refused_when_the_token_fails_validation()
    {
        using var db = CreateDb();
        var service = CreateService(db, GoogleValidationResult.Fail("Google sign-in could not be verified."));

        var result = await service.SignInWithGoogleAsync("forged-token");

        Assert.False(result.Succeeded);
        Assert.Null(result.Token);
        Assert.Empty(await db.Users.ToListAsync()); // no account created off an unverified token
    }

    /// <summary>
    /// A suspended account must not become reachable just because the person arrived through a
    /// different sign-in route.
    /// </summary>
    [Theory]
    [InlineData(false, UserStatus.Active)]
    [InlineData(true, UserStatus.Suspended)]
    public async Task SignIn_is_refused_for_an_inactive_account(bool isActive, UserStatus status)
    {
        using var db = CreateDb();
        db.Users.Add(new User
        {
            UserId = Guid.NewGuid(),
            Email = ValidEmail,
            ExternalProvider = "Google",
            ExternalProviderId = ValidGoogleSubject,
            FirstName = "Blocked",
            LastName = "User",
            IsActive = isActive,
            Status = status,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var service = CreateService(db, ValidGoogleIdentity());
        var result = await service.SignInWithGoogleAsync("any-token");

        Assert.False(result.Succeeded);
        Assert.Null(result.Token);
    }

    [Fact]
    public async Task SignIn_is_refused_for_an_unknown_account_when_auto_provisioning_is_disabled()
    {
        using var db = CreateDb();
        var service = CreateService(db, ValidGoogleIdentity(), allowAutoProvisioning: false);

        var result = await service.SignInWithGoogleAsync("any-token");

        Assert.False(result.Succeeded);
        Assert.Empty(await db.Users.ToListAsync());
    }

    // ── Test doubles ─────────────────────────────────────────────────────────────
    // Hand-written rather than mocked: the test project has no mocking library, and these
    // interfaces are small enough that a stub is clearer than a framework would be.

    private sealed class StubGoogleValidator : IGoogleIdTokenValidator
    {
        private readonly GoogleValidationResult _result;

        public StubGoogleValidator(GoogleValidationResult result) => _result = result;

        public bool IsConfigured() => true;

        public Task<GoogleValidationResult> ValidateAsync(string idToken, CancellationToken ct = default) =>
            Task.FromResult(_result);
    }

    private sealed class StubJwtTokenService : IJwtTokenService
    {
        public string GenerateToken(User user) => $"test-token-for-{user.UserId}";
    }

    private sealed class NoOpAuditLogger : IAuditLogger
    {
        public Task LogAsync(Guid? userId, string action, string? entity = null, string? entityId = null, string? metadata = null) =>
            Task.CompletedTask;
    }
}
