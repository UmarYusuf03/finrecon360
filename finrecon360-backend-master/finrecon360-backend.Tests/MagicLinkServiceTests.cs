using System.Reflection;
using System.Linq;
using finrecon360_backend.Data;
using finrecon360_backend.Models;
using finrecon360_backend.Options;
using finrecon360_backend.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace finrecon360_backend.Tests;

public class MagicLinkServiceTests
{
    private static MagicLinkService CreateService(AppDbContext dbContext, int maxAttempts = 3)
    {
        var magicOptions = Microsoft.Extensions.Options.Options.Create(new MagicLinkOptions
        {
            ExpiresMinutes = 10,
            MaxAttempts = maxAttempts,
            ResendCooldownSeconds = 0,
            FrontendBaseUrl = "http://localhost:4200"
        });
        var jwtOptions = Microsoft.Extensions.Options.Options.Create(new JwtSettings
        {
            Key = "test-signing-key-should-be-long-32chars",
            Issuer = "test-issuer",
            Audience = "test-audience",
            ExpiresMinutes = 60
        });
        return new MagicLinkService(dbContext, magicOptions, jwtOptions);
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"MagicLink-{Guid.NewGuid()}")
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task Hashing_is_deterministic_for_same_token()
    {
        await using var db = CreateDbContext();
        var service = CreateService(db);

        var method = typeof(MagicLinkService).GetMethod("ComputeHash", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var token = "test-token-value";
        var hash1 = (byte[])method!.Invoke(service, new object[] { token })!;
        var hash2 = (byte[])method.Invoke(service, new object[] { token })!;

        Assert.Equal(hash1, hash2);
        Assert.False(hash1.SequenceEqual(System.Text.Encoding.UTF8.GetBytes(token)));
    }

    [Fact]
    public async Task Token_is_single_use_and_expires()
    {
        await using var db = CreateDbContext();
        var service = CreateService(db);

        var token = await service.CreateTokenAsync("user@test.local", Guid.NewGuid(), MagicLinkPurpose.EmailVerify, null);
        Assert.NotNull(token);

        var firstConsume = await service.ConsumeTokenAsync(token!.Token, MagicLinkPurpose.EmailVerify);
        Assert.True(firstConsume.Success);

        var secondConsume = await service.ConsumeTokenAsync(token.Token, MagicLinkPurpose.EmailVerify);
        Assert.False(secondConsume.Success);

        var record = await db.AuthActionTokens.FirstAsync();
        record.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        record.ConsumedAt = null;
        await db.SaveChangesAsync();

        var expiredConsume = await service.ConsumeTokenAsync(token.Token, MagicLinkPurpose.EmailVerify);
        Assert.False(expiredConsume.Success);
    }

    [Fact]
    public async Task Attempt_count_increments_and_locks_after_max()
    {
        await using var db = CreateDbContext();
        var service = CreateService(db, maxAttempts: 2);

        var token = await service.CreateTokenAsync("user@test.local", Guid.NewGuid(), MagicLinkPurpose.PasswordReset, null);
        Assert.NotNull(token);

        var record = await db.AuthActionTokens.FirstAsync();
        record.AttemptCount = 2;
        record.ExpiresAt = DateTime.UtcNow.AddMinutes(5);
        await db.SaveChangesAsync();

        var consume = await service.ConsumeTokenAsync(token!.Token, MagicLinkPurpose.PasswordReset);
        Assert.False(consume.Success);

        var updated = await db.AuthActionTokens.FirstAsync();
        Assert.Equal(3, updated.AttemptCount);
    }
}
