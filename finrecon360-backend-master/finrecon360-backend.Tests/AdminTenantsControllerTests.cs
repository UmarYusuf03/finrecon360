using finrecon360_backend.Controllers.Admin;
using finrecon360_backend.Data;
using finrecon360_backend.Dtos.Admin;
using finrecon360_backend.Models;
using finrecon360_backend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace finrecon360_backend.Tests;

public class AdminTenantsControllerTests
{
    [Fact]
    public async Task ReplaceAdmins_returns_conflict_when_email_is_already_in_another_tenant()
    {
        await using var db = CreateDbContext();
        var now = DateTime.UtcNow;
        var actorId = Guid.NewGuid();
        var tenantA = new Tenant { TenantId = Guid.NewGuid(), Name = "Tenant A", Status = TenantStatus.Active, CreatedAt = now };
        var tenantB = new Tenant { TenantId = Guid.NewGuid(), Name = "Tenant B", Status = TenantStatus.Active, CreatedAt = now };
        var sharedUser = BuildUser("shared-admin@test.local", now);

        db.Tenants.AddRange(tenantA, tenantB);
        db.Users.Add(sharedUser);
        db.TenantUsers.Add(new TenantUser
        {
            TenantUserId = Guid.NewGuid(),
            TenantId = tenantB.TenantId,
            UserId = sharedUser.UserId,
            Role = TenantUserRole.TenantAdmin,
            CreatedAt = now
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, actorId);
        var result = await controller.ReplaceAdmins(tenantA.TenantId, new TenantAdminSetRequest
        {
            Emails = new[] { sharedUser.Email }
        });

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Contains("already assigned to another tenant", conflict.Value?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReplaceAdmins_allows_user_not_assigned_to_other_tenants()
    {
        await using var db = CreateDbContext();
        var now = DateTime.UtcNow;
        var actorId = Guid.NewGuid();
        var tenantA = new Tenant { TenantId = Guid.NewGuid(), Name = "Tenant A", Status = TenantStatus.Active, CreatedAt = now };
        var localUser = BuildUser("tenant-admin@test.local", now);

        db.Tenants.Add(tenantA);
        db.Users.Add(localUser);
        await db.SaveChangesAsync();

        var controller = CreateController(db, actorId);
        var result = await controller.ReplaceAdmins(tenantA.TenantId, new TenantAdminSetRequest
        {
            Emails = new[] { localUser.Email }
        });

        Assert.IsType<NoContentResult>(result);
        var membership = await db.TenantUsers.AsNoTracking()
            .SingleAsync(x => x.TenantId == tenantA.TenantId && x.UserId == localUser.UserId);
        Assert.Equal(TenantUserRole.TenantAdmin, membership.Role);
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"AdminTenants-{Guid.NewGuid()}")
            .Options;
        return new AppDbContext(options);
    }

    private static User BuildUser(string email, DateTime now)
    {
        return new User
        {
            UserId = Guid.NewGuid(),
            Email = email,
            DisplayName = email,
            FirstName = "Test",
            LastName = "User",
            Country = "US",
            Gender = "na",
            PasswordHash = "hash",
            CreatedAt = now,
            IsActive = true,
            Status = UserStatus.Active
        };
    }

    private static AdminTenantsController CreateController(AppDbContext db, Guid actorId)
    {
        return new AdminTenantsController(
            db,
            new StubUserContext(actorId),
            new StubAuditLogger(),
            new Sha256PasswordHasher(),
            new StubTenantUserDirectoryService(),
            new StubSystemEnforcementService());
    }

    private sealed class StubUserContext : IUserContext
    {
        public StubUserContext(Guid userId)
        {
            UserId = userId;
        }

        public Guid? UserId { get; }
        public string? Email => "actor@test.local";
        public bool IsAuthenticated => true;
        public bool IsActive => true;
        public UserStatus? Status => UserStatus.Active;
    }

    private sealed class StubAuditLogger : IAuditLogger
    {
        public Task LogAsync(Guid? userId, string action, string? entity = null, string? entityId = null, string? metadata = null)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class StubTenantUserDirectoryService : ITenantUserDirectoryService
    {
        public Task UpsertTenantUserAsync(Guid tenantId, User user, TenantUserRole role, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task ReplaceTenantAdminsAsync(Guid tenantId, IReadOnlyCollection<User> admins, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class StubSystemEnforcementService : ISystemEnforcementService
    {
        public Task<EnforcementApplyResult> ApplyTenantActionAsync(Guid tenantId, TenantStatus newStatus, EnforcementActionType actionType, EnforcementActionRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(EnforcementApplyResult.Success);

        public Task<EnforcementApplyResult> ReinstateTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
            => Task.FromResult(EnforcementApplyResult.Success);

        public Task<EnforcementApplyResult> ApplyUserActionAsync(Guid tenantId, Guid userId, UserStatus newStatus, EnforcementActionType actionType, EnforcementActionRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(EnforcementApplyResult.Success);

        public Task<EnforcementApplyResult> ReinstateUserAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult(EnforcementApplyResult.Success);
    }
}
