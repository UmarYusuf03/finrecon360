using finrecon360_backend.Controllers.Admin;
using finrecon360_backend.Data;
using finrecon360_backend.Dtos.Admin;
using finrecon360_backend.Models;
using finrecon360_backend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace finrecon360_backend.Tests;

public class AdminUsersControllerTests
{
    [Fact]
    public async Task ReplaceUserRoles_blocks_removing_ADMIN_from_last_tenant_admin()
    {
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        await using var appDb = CreateAppDb();
        var tenantDbName = $"TenantDb-{Guid.NewGuid()}";
        await SeedAppDbAsync(appDb, tenantId, actorId, actorId);
        await SeedTenantDbAsync(tenantDbName, actorId, actorId);

        var controller = CreateController(appDb, tenantId, actorId, tenantDbName);
        var request = new AdminUserRoleSetRequest
        {
            RoleCodes = new[] { "USER" }
        };

        var result = await controller.ReplaceUserRoles(actorId, request);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Contains("last tenant admin", conflict.Value?.ToString(), StringComparison.OrdinalIgnoreCase);

        var membership = await appDb.TenantUsers.AsNoTracking().SingleAsync(x => x.TenantId == tenantId && x.UserId == actorId);
        Assert.Equal(TenantUserRole.TenantAdmin, membership.Role);
    }

    [Fact]
    public async Task ReplaceUserRoles_allows_removing_ADMIN_when_another_tenant_admin_exists()
    {
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var targetId = Guid.NewGuid();

        await using var appDb = CreateAppDb();
        var tenantDbName = $"TenantDb-{Guid.NewGuid()}";
        await SeedAppDbAsync(appDb, tenantId, actorId, targetId);
        await SeedTenantDbAsync(tenantDbName, actorId, targetId);

        var controller = CreateController(appDb, tenantId, actorId, tenantDbName);
        var request = new AdminUserRoleSetRequest
        {
            RoleCodes = new[] { "USER" }
        };

        var result = await controller.ReplaceUserRoles(targetId, request);
        Assert.IsType<NoContentResult>(result);

        var targetMembership = await appDb.TenantUsers.AsNoTracking().SingleAsync(x => x.TenantId == tenantId && x.UserId == targetId);
        Assert.Equal(TenantUserRole.TenantUser, targetMembership.Role);
    }

    private static AdminUsersController CreateController(AppDbContext appDb, Guid tenantId, Guid actorId, string tenantDbName)
    {
        return new AdminUsersController(
            appDb,
            new Sha256PasswordHasher(),
            new StubUserContext(actorId),
            new StubTenantContext(tenantId),
            new InMemoryTenantDbContextFactory(tenantDbName));
    }

    private static AppDbContext CreateAppDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"AppDb-{Guid.NewGuid()}")
            .Options;
        return new AppDbContext(options);
    }

    private static async Task SeedAppDbAsync(AppDbContext appDb, Guid tenantId, Guid actorId, Guid targetId)
    {
        var now = DateTime.UtcNow;
        var tenant = new Tenant
        {
            TenantId = tenantId,
            Name = "Tenant One",
            Status = TenantStatus.Active,
            CreatedAt = now
        };

        var actor = BuildUser(actorId, "actor@test.local", now);

        appDb.Tenants.Add(tenant);
        appDb.Users.Add(actor);
        if (targetId != actorId)
        {
            appDb.Users.Add(BuildUser(targetId, "target@test.local", now));
        }

        appDb.TenantUsers.Add(new TenantUser
        {
            TenantUserId = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = actorId,
            Role = TenantUserRole.TenantAdmin,
            CreatedAt = now
        });

        if (targetId != actorId)
        {
            appDb.TenantUsers.Add(new TenantUser
            {
                TenantUserId = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = targetId,
                Role = TenantUserRole.TenantAdmin,
                CreatedAt = now
            });
        }

        await appDb.SaveChangesAsync();
    }

    private static User BuildUser(Guid id, string email, DateTime now)
    {
        return new User
        {
            UserId = id,
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

    private static async Task SeedTenantDbAsync(string tenantDbName, Guid actorId, Guid targetId)
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseInMemoryDatabase(tenantDbName)
            .Options;

        await using var tenantDb = new TenantDbContext(options);
        var now = DateTime.UtcNow;
        var adminRoleId = Guid.NewGuid();
        var userRoleId = Guid.NewGuid();

        tenantDb.Roles.Add(new TenantRole
        {
            RoleId = adminRoleId,
            Code = "ADMIN",
            Name = "Admin",
            IsSystem = true,
            IsActive = true,
            CreatedAt = now
        });
        tenantDb.Roles.Add(new TenantRole
        {
            RoleId = userRoleId,
            Code = "USER",
            Name = "User",
            IsSystem = true,
            IsActive = true,
            CreatedAt = now
        });
        tenantDb.TenantUsers.Add(new TenantScopedUser
        {
            TenantUserId = Guid.NewGuid(),
            UserId = actorId,
            Email = "actor@test.local",
            DisplayName = "Actor",
            Role = TenantUserRole.TenantAdmin.ToString(),
            Status = UserStatus.Active.ToString(),
            IsActive = true,
            CreatedAt = now
        });

        if (targetId != actorId)
        {
            tenantDb.TenantUsers.Add(new TenantScopedUser
            {
                TenantUserId = Guid.NewGuid(),
                UserId = targetId,
                Email = "target@test.local",
                DisplayName = "Target",
                Role = TenantUserRole.TenantAdmin.ToString(),
                Status = UserStatus.Active.ToString(),
                IsActive = true,
                CreatedAt = now
            });
        }

        tenantDb.UserRoles.Add(new TenantUserRoleAssignment
        {
            UserId = actorId,
            RoleId = adminRoleId,
            AssignedAt = now
        });

        if (targetId != actorId)
        {
            tenantDb.UserRoles.Add(new TenantUserRoleAssignment
            {
                UserId = targetId,
                RoleId = adminRoleId,
                AssignedAt = now
            });
        }

        await tenantDb.SaveChangesAsync();
    }

    private sealed class StubUserContext : IUserContext
    {
        public StubUserContext(Guid userId)
        {
            UserId = userId;
        }

        public Guid? UserId { get; }
        public string? Email => "stub@test.local";
        public bool IsAuthenticated => true;
        public bool IsActive => true;
        public UserStatus? Status => UserStatus.Active;
    }

    private sealed class StubTenantContext : ITenantContext
    {
        private readonly Guid _tenantId;

        public StubTenantContext(Guid tenantId)
        {
            _tenantId = tenantId;
        }

        public Task<TenantResolution?> ResolveAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<TenantResolution?>(new TenantResolution(_tenantId, TenantStatus.Active, "Tenant One"));
        }
    }

    private sealed class InMemoryTenantDbContextFactory : ITenantDbContextFactory
    {
        private readonly string _databaseName;

        public InMemoryTenantDbContextFactory(string databaseName)
        {
            _databaseName = databaseName;
        }

        public Task<TenantDbContext> CreateAsync(Guid tenantId, CancellationToken cancellationToken = default)
        {
            var options = new DbContextOptionsBuilder<TenantDbContext>()
                .UseInMemoryDatabase(_databaseName)
                .Options;

            return Task.FromResult(new TenantDbContext(options));
        }
    }
}
