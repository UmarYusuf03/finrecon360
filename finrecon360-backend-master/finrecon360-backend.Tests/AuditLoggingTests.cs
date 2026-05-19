using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using finrecon360_backend.Controllers.Admin;
using finrecon360_backend.Data;
using finrecon360_backend.Dtos;
using finrecon360_backend.Dtos.Admin;
using finrecon360_backend.Models;
using finrecon360_backend.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace finrecon360_backend.Tests;

public class AuditLoggingTests
{
    [Fact]
    public async Task AuditLogger_writes_audit_log_entry()
    {
        await using var db = CreateAppDb();
        var logger = new AuditLogger(db);
        var userId = Guid.NewGuid();

        await logger.LogAsync(userId, "TestAction", "Entity", "123", "meta");

        var entry = await db.AuditLogs.FirstOrDefaultAsync();
        Assert.NotNull(entry);
        Assert.Equal(userId, entry!.UserId);
        Assert.Equal("TestAction", entry.Action);
        Assert.Equal("Entity", entry.Entity);
        Assert.Equal("123", entry.EntityId);
        Assert.Equal("meta", entry.Metadata);
    }

    [Fact]
    public async Task Tenant_audit_logs_are_scoped_to_tenant()
    {
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        await using var db = CreateAppDb();

        await SeedTenantAuditDataAsync(db, tenantId, otherTenantId, userId, otherUserId);

        var controller = new AdminTenantAuditLogsController(
            db,
            new StubTenantContext(tenantId),
            new StubUserContext(userId));

        var result = await controller.GetAuditLogs();
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<PagedResult<AuditLogSummaryDto>>(ok.Value);

        Assert.Single(dto.Items);
        Assert.Equal(userId, dto.Items[0].UserId);
    }

    [Fact]
    public async Task System_audit_logs_require_system_admin()
    {
        await using var db = CreateAppDb();
        var userId = Guid.NewGuid();
        db.Users.Add(new User
        {
            UserId = userId,
            Email = "user@test.local",
            DisplayName = "User",
            FirstName = "User",
            LastName = "Test",
            Country = "LK",
            Gender = "NA",
            PasswordHash = "hash",
            CreatedAt = DateTime.UtcNow,
            IsActive = true,
            Status = UserStatus.Active,
            IsSystemAdmin = false
        });
        await db.SaveChangesAsync();

        var controller = new AdminAuditLogsController(db);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = BuildHttpContext(userId)
        };

        var result = await controller.GetAuditLogs();
        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task System_audit_logs_return_for_system_admin()
    {
        await using var db = CreateAppDb();
        var userId = Guid.NewGuid();
        db.Users.Add(new User
        {
            UserId = userId,
            Email = "admin@test.local",
            DisplayName = "Admin",
            FirstName = "Admin",
            LastName = "Test",
            Country = "LK",
            Gender = "NA",
            PasswordHash = "hash",
            CreatedAt = DateTime.UtcNow,
            IsActive = true,
            Status = UserStatus.Active,
            IsSystemAdmin = true
        });

        db.AuditLogs.Add(new AuditLog
        {
            AuditLogId = Guid.NewGuid(),
            UserId = userId,
            Action = "Login",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var controller = new AdminAuditLogsController(db);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = BuildHttpContext(userId)
        };

        var result = await controller.GetAuditLogs();
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<PagedResult<AuditLogSummaryDto>>(ok.Value);

        Assert.Single(dto.Items);
    }

    private static async Task SeedTenantAuditDataAsync(
        AppDbContext db,
        Guid tenantId,
        Guid otherTenantId,
        Guid userId,
        Guid otherUserId)
    {
        var now = DateTime.UtcNow;

        db.Tenants.AddRange(
            new Tenant { TenantId = tenantId, Name = "Tenant A", Status = TenantStatus.Active, CreatedAt = now },
            new Tenant { TenantId = otherTenantId, Name = "Tenant B", Status = TenantStatus.Active, CreatedAt = now }
        );

        db.Users.AddRange(
            new User
            {
                UserId = userId,
                Email = "tenant-a@test.local",
                DisplayName = "Tenant A",
                FirstName = "Tenant",
                LastName = "A",
                Country = "LK",
                Gender = "NA",
                PasswordHash = "hash",
                CreatedAt = now,
                IsActive = true,
                Status = UserStatus.Active
            },
            new User
            {
                UserId = otherUserId,
                Email = "tenant-b@test.local",
                DisplayName = "Tenant B",
                FirstName = "Tenant",
                LastName = "B",
                Country = "LK",
                Gender = "NA",
                PasswordHash = "hash",
                CreatedAt = now,
                IsActive = true,
                Status = UserStatus.Active
            }
        );

        db.TenantUsers.AddRange(
            new TenantUser
            {
                TenantUserId = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = userId,
                Role = TenantUserRole.TenantAdmin,
                CreatedAt = now
            },
            new TenantUser
            {
                TenantUserId = Guid.NewGuid(),
                TenantId = otherTenantId,
                UserId = otherUserId,
                Role = TenantUserRole.TenantAdmin,
                CreatedAt = now
            }
        );

        db.AuditLogs.AddRange(
            new AuditLog
            {
                AuditLogId = Guid.NewGuid(),
                UserId = userId,
                Action = "TenantAAction",
                CreatedAt = now
            },
            new AuditLog
            {
                AuditLogId = Guid.NewGuid(),
                UserId = otherUserId,
                Action = "TenantBAction",
                CreatedAt = now
            }
        );

        await db.SaveChangesAsync();
    }

    private static DefaultHttpContext BuildHttpContext(Guid userId)
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("sub", userId.ToString())
        }, "TestAuth");
        var principal = new ClaimsPrincipal(identity);
        return new DefaultHttpContext { User = principal };
    }

    private static AppDbContext CreateAppDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"AppDb-Audit-{Guid.NewGuid()}")
            .Options;
        return new AppDbContext(options);
    }

    private sealed class StubUserContext : IUserContext
    {
        public StubUserContext(Guid userId)
        {
            UserId = userId;
        }

        public Guid? UserId { get; }
        public string? Email => "tenant-a@test.local";
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
            return Task.FromResult<TenantResolution?>(new TenantResolution(_tenantId, TenantStatus.Active, "Tenant A"));
        }
    }
}
