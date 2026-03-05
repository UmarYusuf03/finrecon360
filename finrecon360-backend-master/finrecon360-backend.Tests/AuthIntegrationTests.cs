using System.Net;
using System.Net.Http.Json;
using System.Linq;
using finrecon360_backend.Data;
using finrecon360_backend.Dtos.Auth;
using finrecon360_backend.Models;
using finrecon360_backend.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace finrecon360_backend.Tests;

public class AuthIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public AuthIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }


    [Fact]
    public async Task Register_sends_email_and_persists_token()
    {
        _factory.EmailSender.Requests.Clear();
        using var client = _factory.CreateClient();
        var payload = new RegisterRequest(
            "newuser@test.local",
            "New",
            "User",
            "US",
            "male",
            "Password123!",
            "Password123!");

        var response = await client.PostAsJsonAsync("/api/auth/register", payload);
        response.EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.True(await db.AuthActionTokens.AnyAsync(t => t.Email == "newuser@test.local" && t.Purpose == MagicLinkPurpose.EmailVerify));
        Assert.Single(_factory.EmailSender.Requests);
    }

    [Fact]
    public async Task Verify_email_consumes_token_and_sets_confirmed()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var magicService = scope.ServiceProvider.GetRequiredService<IMagicLinkService>();

        var user = new User
        {
            UserId = Guid.NewGuid(),
            Email = "verify@test.local",
            FirstName = "Verify",
            LastName = "User",
            Country = "US",
            Gender = "male",
            PasswordHash = "hash",
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var token = await magicService.CreateTokenAsync(user.Email, user.UserId, MagicLinkPurpose.EmailVerify, null);
        Assert.NotNull(token);

        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/verify-email-link", new VerifyEmailLinkRequest { Token = token!.Token });
        response.EnsureSuccessStatusCode();

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var updated = await verifyDb.Users.AsNoTracking().FirstAsync(u => u.UserId == user.UserId);
        Assert.True(updated.EmailConfirmed);
    }

    [Fact]
    public async Task Request_password_reset_does_not_reveal_email_existence()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Users.Add(new User
        {
            UserId = Guid.NewGuid(),
            Email = "known@test.local",
            FirstName = "Known",
            LastName = "User",
            Country = "US",
            Gender = "male",
            PasswordHash = "hash",
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        });
        await db.SaveChangesAsync();

        using var client = _factory.CreateClient();

        var known = await client.PostAsJsonAsync("/api/auth/request-password-reset-link", new RequestPasswordResetLinkRequest { Email = "known@test.local" });
        var unknown = await client.PostAsJsonAsync("/api/auth/request-password-reset-link", new RequestPasswordResetLinkRequest { Email = "missing@test.local" });

        var knownBody = await known.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        var unknownBody = await unknown.Content.ReadFromJsonAsync<Dictionary<string, string>>();

        Assert.Equal(HttpStatusCode.OK, known.StatusCode);
        Assert.Equal(HttpStatusCode.OK, unknown.StatusCode);
        Assert.Equal(knownBody?["message"], unknownBody?["message"]);
    }

    [Fact]
    public async Task Admin_endpoints_require_permissions()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = new User
        {
            UserId = Guid.NewGuid(),
            Email = "noperm@test.local",
            FirstName = "No",
            LastName = "Perm",
            Country = "US",
            Gender = "male",
            PasswordHash = "hash",
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        using var client = TestWebApplicationFactory.CreateAuthenticatedClient(_factory, user.UserId, user.Email);
        var response = await client.GetAsync("/api/admin/roles");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Me_returns_computed_permissions()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = new User
        {
            UserId = Guid.NewGuid(),
            Email = "perm@test.local",
            FirstName = "Perm",
            LastName = "User",
            Country = "US",
            Gender = "male",
            PasswordHash = "hash",
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };
        var role = new Role
        {
            RoleId = Guid.NewGuid(),
            Code = "TEST",
            Name = "Test",
            IsSystem = false,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        var permission = new Permission
        {
            PermissionId = Guid.NewGuid(),
            Code = "ADMIN.USERS.MANAGE",
            Name = "User Management",
            CreatedAt = DateTime.UtcNow
        };

        db.Users.Add(user);
        db.Roles.Add(role);
        db.Permissions.Add(permission);
        db.UserRoles.Add(new UserRole { UserId = user.UserId, RoleId = role.RoleId, AssignedAt = DateTime.UtcNow });
        db.RolePermissions.Add(new RolePermission { RoleId = role.RoleId, PermissionId = permission.PermissionId, GrantedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        using var client = TestWebApplicationFactory.CreateAuthenticatedClient(_factory, user.UserId, user.Email);
        var response = await client.GetAsync("/api/me");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        Assert.NotNull(payload);
        var permissions = payload!["permissions"] as System.Text.Json.JsonElement?;
        Assert.True(permissions.HasValue);
        Assert.Contains("ADMIN.USERS.MANAGE", permissions.Value.EnumerateArray().Select(p => p.GetString()));
    }

    [Fact]
    public async Task Tenant_admin_cannot_create_user_when_plan_limit_reached()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var actor = new User
        {
            UserId = Guid.NewGuid(),
            Email = "tenantadmin@test.local",
            FirstName = "Tenant",
            LastName = "Admin",
            Country = "US",
            Gender = "male",
            PasswordHash = "hash",
            CreatedAt = DateTime.UtcNow,
            IsActive = true,
            Status = UserStatus.Active
        };

        var role = new Role
        {
            RoleId = Guid.NewGuid(),
            Code = "TENANT_ADMIN",
            Name = "Tenant Admin",
            IsSystem = false,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        var permission = new Permission
        {
            PermissionId = Guid.NewGuid(),
            Code = "USER_MANAGEMENT",
            Name = "User Management",
            CreatedAt = DateTime.UtcNow
        };

        var tenant = new Tenant
        {
            TenantId = Guid.NewGuid(),
            Name = "Tenant A",
            Status = TenantStatus.Active,
            CreatedAt = DateTime.UtcNow
        };

        var plan = new Plan
        {
            PlanId = Guid.NewGuid(),
            Code = "LIMIT1",
            Name = "Limit 1",
            DurationDays = 30,
            MaxAccounts = 1,
            PriceCents = 0,
            Currency = "USD",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        var sub = new Subscription
        {
            SubscriptionId = Guid.NewGuid(),
            TenantId = tenant.TenantId,
            PlanId = plan.PlanId,
            Status = SubscriptionStatus.Active,
            CurrentPeriodStart = DateTime.UtcNow,
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(30),
            CreatedAt = DateTime.UtcNow
        };
        tenant.CurrentSubscriptionId = sub.SubscriptionId;

        db.Users.Add(actor);
        db.Roles.Add(role);
        db.Permissions.Add(permission);
        db.Tenants.Add(tenant);
        db.Plans.Add(plan);
        db.Subscriptions.Add(sub);
        db.UserRoles.Add(new UserRole { UserId = actor.UserId, RoleId = role.RoleId, AssignedAt = DateTime.UtcNow });
        db.RolePermissions.Add(new RolePermission { RoleId = role.RoleId, PermissionId = permission.PermissionId, GrantedAt = DateTime.UtcNow });
        db.TenantUsers.Add(new TenantUser
        {
            TenantUserId = Guid.NewGuid(),
            TenantId = tenant.TenantId,
            UserId = actor.UserId,
            Role = TenantUserRole.TenantAdmin,
            CreatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();

        using var client = TestWebApplicationFactory.CreateAuthenticatedClient(_factory, actor.UserId, actor.Email);
        client.DefaultRequestHeaders.Add("X-Tenant-Id", tenant.TenantId.ToString());

        var payload = new
        {
            email = "newmember@test.local",
            displayName = "New Member",
            password = "Password123!",
            roleCodes = new[] { "TENANT_ADMIN" }
        };

        var response = await client.PostAsJsonAsync("/api/admin/users", payload);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Tenant_admin_cannot_access_system_user_enforcement_endpoint()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tenant = new Tenant
        {
            TenantId = Guid.NewGuid(),
            Name = "Tenant X",
            Status = TenantStatus.Active,
            CreatedAt = DateTime.UtcNow
        };

        var actor = new User
        {
            UserId = Guid.NewGuid(),
            Email = "tenantadmin-enforce@test.local",
            FirstName = "Tenant",
            LastName = "Admin",
            Country = "US",
            Gender = "male",
            PasswordHash = "hash",
            CreatedAt = DateTime.UtcNow,
            IsActive = true,
            Status = UserStatus.Active
        };

        var target = new User
        {
            UserId = Guid.NewGuid(),
            Email = "target@test.local",
            FirstName = "Target",
            LastName = "User",
            Country = "US",
            Gender = "male",
            PasswordHash = "hash",
            CreatedAt = DateTime.UtcNow,
            IsActive = true,
            Status = UserStatus.Active
        };

        db.Tenants.Add(tenant);
        db.Users.AddRange(actor, target);
        db.TenantUsers.Add(new TenantUser
        {
            TenantUserId = Guid.NewGuid(),
            TenantId = tenant.TenantId,
            UserId = actor.UserId,
            Role = TenantUserRole.TenantAdmin,
            CreatedAt = DateTime.UtcNow
        });
        db.TenantUsers.Add(new TenantUser
        {
            TenantUserId = Guid.NewGuid(),
            TenantId = tenant.TenantId,
            UserId = target.UserId,
            Role = TenantUserRole.TenantUser,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        using var client = TestWebApplicationFactory.CreateAuthenticatedClient(_factory, actor.UserId, actor.Email);
        var response = await client.PostAsJsonAsync($"/api/system/enforcement/tenants/{tenant.TenantId}/users/{target.UserId}/suspend", new { reason = "test" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task System_admin_can_suspend_ban_and_reinstate_user_via_enforcement_endpoint()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tenant = new Tenant
        {
            TenantId = Guid.NewGuid(),
            Name = "Tenant Y",
            Status = TenantStatus.Active,
            CreatedAt = DateTime.UtcNow
        };

        var systemAdmin = new User
        {
            UserId = Guid.NewGuid(),
            Email = "sysadmin-enforce@test.local",
            FirstName = "System",
            LastName = "Admin",
            Country = "US",
            Gender = "male",
            PasswordHash = "hash",
            CreatedAt = DateTime.UtcNow,
            IsActive = true,
            IsSystemAdmin = true,
            Status = UserStatus.Active
        };

        var target = new User
        {
            UserId = Guid.NewGuid(),
            Email = "target2@test.local",
            FirstName = "Target",
            LastName = "Two",
            Country = "US",
            Gender = "male",
            PasswordHash = "hash",
            CreatedAt = DateTime.UtcNow,
            IsActive = true,
            Status = UserStatus.Active
        };

        var role = new Role
        {
            RoleId = Guid.NewGuid(),
            Code = "SYS_ENFORCER",
            Name = "System Enforcer",
            IsSystem = true,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        var permission = new Permission
        {
            PermissionId = Guid.NewGuid(),
            Code = "ADMIN.ENFORCEMENT.MANAGE",
            Name = "Enforcement",
            CreatedAt = DateTime.UtcNow
        };

        db.Tenants.Add(tenant);
        db.Users.AddRange(systemAdmin, target);
        db.Roles.Add(role);
        db.Permissions.Add(permission);
        db.UserRoles.Add(new UserRole { UserId = systemAdmin.UserId, RoleId = role.RoleId, AssignedAt = DateTime.UtcNow });
        db.RolePermissions.Add(new RolePermission { RoleId = role.RoleId, PermissionId = permission.PermissionId, GrantedAt = DateTime.UtcNow });
        db.TenantUsers.Add(new TenantUser
        {
            TenantUserId = Guid.NewGuid(),
            TenantId = tenant.TenantId,
            UserId = target.UserId,
            Role = TenantUserRole.TenantUser,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        using var client = TestWebApplicationFactory.CreateAuthenticatedClient(_factory, systemAdmin.UserId, systemAdmin.Email);

        var suspend = await client.PostAsJsonAsync($"/api/system/enforcement/tenants/{tenant.TenantId}/users/{target.UserId}/suspend", new { reason = "policy" });
        Assert.Equal(HttpStatusCode.NoContent, suspend.StatusCode);

        var ban = await client.PostAsJsonAsync($"/api/system/enforcement/tenants/{tenant.TenantId}/users/{target.UserId}/ban", new { reason = "fraud" });
        Assert.Equal(HttpStatusCode.NoContent, ban.StatusCode);

        using (var checkScope = _factory.Services.CreateScope())
        {
            var checkDb = checkScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var updated = await checkDb.Users.AsNoTracking().FirstAsync(u => u.UserId == target.UserId);
            Assert.Equal(UserStatus.Banned, updated.Status);

            var enforcementCount = await checkDb.EnforcementActions.CountAsync(e =>
                e.TargetType == EnforcementTargetType.User &&
                e.TargetId == target.UserId);
            Assert.Equal(2, enforcementCount);
        }

        using (var prepScope = _factory.Services.CreateScope())
        {
            var prepDb = prepScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await prepDb.Users.FirstAsync(u => u.UserId == target.UserId);
            user.Status = UserStatus.Suspended;
            await prepDb.SaveChangesAsync();
        }

        var reinstate = await client.PostAsync($"/api/system/enforcement/tenants/{tenant.TenantId}/users/{target.UserId}/reinstate", null);
        Assert.Equal(HttpStatusCode.NoContent, reinstate.StatusCode);

        using var finalScope = _factory.Services.CreateScope();
        var finalDb = finalScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reinstated = await finalDb.Users.AsNoTracking().FirstAsync(u => u.UserId == target.UserId);
        Assert.Equal(UserStatus.Active, reinstated.Status);
    }
}
