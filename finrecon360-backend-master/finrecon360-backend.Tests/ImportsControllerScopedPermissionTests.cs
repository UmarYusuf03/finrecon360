using System.Text;
using finrecon360_backend.Controllers;
using finrecon360_backend.Data;
using finrecon360_backend.Dtos.Imports;
using finrecon360_backend.Models;
using finrecon360_backend.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace finrecon360_backend.Tests;

/// <summary>
/// Covers the ImportsController authorization rewrite: TenantAdmin remains a full bypass,
/// but a non-admin tenant member is now authorized per source type against the
/// ADMIN.IMPORTS.&lt;SOURCE&gt;.&lt;ACTION&gt; grants the permission matrix's "Scoped
/// Permissions" section offers, instead of being rejected outright.
/// </summary>
public class ImportsControllerScopedPermissionTests
{
    [Fact]
    public async Task Non_admin_with_scoped_POS_grant_can_upload_a_POS_file()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var appDb = CreateAppDb();
        var tenantDbName = $"TenantImportsScoped-{Guid.NewGuid()}";

        await SeedAsync(appDb, tenantDbName, tenantId, userId, grantedPermissionCodes: ["ADMIN.IMPORTS.POS.CREATE"]);

        var controller = CreateController(appDb, tenantDbName, tenantId, userId);

        var result = await controller.Upload(CreateFormFile("pos.csv", Encoding.UTF8.GetBytes("a,b\n1,2")), "POS");

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<ImportUploadResponseDto>(ok.Value);
        Assert.Equal("POS", dto.SourceType);
    }

    [Fact]
    public async Task Non_admin_with_scoped_POS_grant_cannot_upload_an_ERP_file()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var appDb = CreateAppDb();
        var tenantDbName = $"TenantImportsScoped-{Guid.NewGuid()}";

        await SeedAsync(appDb, tenantDbName, tenantId, userId, grantedPermissionCodes: ["ADMIN.IMPORTS.POS.CREATE"]);

        var controller = CreateController(appDb, tenantDbName, tenantId, userId);

        var result = await controller.Upload(CreateFormFile("erp.csv", Encoding.UTF8.GetBytes("a,b\n1,2")), "ERP");

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task Non_admin_with_no_import_grants_cannot_upload_anything()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var appDb = CreateAppDb();
        var tenantDbName = $"TenantImportsScoped-{Guid.NewGuid()}";

        await SeedAsync(appDb, tenantDbName, tenantId, userId, grantedPermissionCodes: []);

        var controller = CreateController(appDb, tenantDbName, tenantId, userId);

        var result = await controller.Upload(CreateFormFile("pos.csv", Encoding.UTF8.GetBytes("a,b\n1,2")), "POS");

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task Non_admin_with_scoped_POS_grant_only_sees_POS_batches_in_history()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var appDb = CreateAppDb();
        var tenantDbName = $"TenantImportsScoped-{Guid.NewGuid()}";

        await SeedAsync(appDb, tenantDbName, tenantId, userId, grantedPermissionCodes: ["ADMIN.IMPORTS.POS.CREATE"]);

        await using (var tenantDb = CreateTenantDb(tenantDbName))
        {
            tenantDb.ImportBatches.AddRange(
                new ImportBatch { ImportBatchId = Guid.NewGuid(), SourceType = "POS", Status = "RECEIVED", ImportedAt = DateTime.UtcNow, RawRecordCount = 0, NormalizedRecordCount = 0 },
                new ImportBatch { ImportBatchId = Guid.NewGuid(), SourceType = "ERP", Status = "RECEIVED", ImportedAt = DateTime.UtcNow, RawRecordCount = 0, NormalizedRecordCount = 0 });
            await tenantDb.SaveChangesAsync();
        }

        var controller = CreateController(appDb, tenantDbName, tenantId, userId);

        var result = await controller.GetHistory();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<ImportHistoryResponseDto>(ok.Value);
        Assert.Single(dto.Items);
        Assert.Equal("POS", dto.Items.Single().SourceType);
    }

    private static ImportsController CreateController(AppDbContext appDb, string tenantDbName, Guid tenantId, Guid userId)
    {
        return new ImportsController(
            appDb,
            new StubTenantContext(tenantId),
            new InMemoryTenantDbContextFactory(tenantDbName),
            new StubUserContext(userId),
            new ImportFileParser(),
            new StubImportNormalizationService(),
            new StubAuditLogger());
    }

    private static AppDbContext CreateAppDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"AppDb-ImportsScoped-{Guid.NewGuid()}")
            .Options;
        return new AppDbContext(options);
    }

    private static TenantDbContext CreateTenantDb(string tenantDbName)
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseInMemoryDatabase(tenantDbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new TenantDbContext(options);
    }

    private static async Task SeedAsync(
        AppDbContext appDb,
        string tenantDbName,
        Guid tenantId,
        Guid userId,
        IReadOnlyCollection<string> grantedPermissionCodes)
    {
        var now = DateTime.UtcNow;

        appDb.Tenants.Add(new Tenant
        {
            TenantId = tenantId,
            Name = "Tenant Imports Scoped",
            Status = TenantStatus.Active,
            CreatedAt = now
        });

        appDb.Users.Add(new User
        {
            UserId = userId,
            Email = "cashier@test.local",
            DisplayName = "Cashier",
            FirstName = "Cashier",
            LastName = "Test",
            Country = "LK",
            Gender = "NA",
            PasswordHash = "hash",
            CreatedAt = now,
            IsActive = true,
            Status = UserStatus.Active
        });

        // WHY TenantUser (not TenantAdmin): the whole point of these tests is verifying the
        // granular scoped-permission path, which only runs for non-admin tenant members.
        appDb.TenantUsers.Add(new TenantUser
        {
            TenantUserId = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            Role = TenantUserRole.TenantUser,
            CreatedAt = now
        });

        await appDb.SaveChangesAsync();

        await using var tenantDb = CreateTenantDb(tenantDbName);

        tenantDb.TenantUsers.Add(new TenantScopedUser
        {
            TenantUserId = Guid.NewGuid(),
            UserId = userId,
            Email = "cashier@test.local",
            DisplayName = "Cashier",
            Role = TenantUserRole.TenantUser.ToString(),
            Status = UserStatus.Active.ToString(),
            IsActive = true,
            CreatedAt = now
        });

        var cashierRoleId = Guid.NewGuid();
        tenantDb.Roles.Add(new TenantRole
        {
            RoleId = cashierRoleId,
            Code = "CASHIER",
            Name = "Cashier",
            IsSystem = false,
            IsActive = true,
            CreatedAt = now
        });

        tenantDb.UserRoles.Add(new TenantUserRoleAssignment
        {
            UserId = userId,
            RoleId = cashierRoleId,
            AssignedAt = now
        });

        foreach (var code in grantedPermissionCodes)
        {
            var permissionId = Guid.NewGuid();
            tenantDb.Permissions.Add(new TenantPermission
            {
                PermissionId = permissionId,
                Code = code,
                Name = code,
                Module = "Imports",
                CreatedAt = now
            });

            tenantDb.RolePermissions.Add(new TenantRolePermission
            {
                RoleId = cashierRoleId,
                PermissionId = permissionId,
                GrantedAt = now
            });
        }

        await tenantDb.SaveChangesAsync();
    }

    private static IFormFile CreateFormFile(string fileName, byte[] content)
    {
        var stream = new MemoryStream(content);
        return new FormFile(stream, 0, content.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = "text/csv"
        };
    }

    private sealed class StubUserContext : IUserContext
    {
        public StubUserContext(Guid userId)
        {
            UserId = userId;
        }

        public Guid? UserId { get; }
        public string? Email => "cashier@test.local";
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
            return Task.FromResult<TenantResolution?>(new TenantResolution(_tenantId, TenantStatus.Active, "Tenant Imports Scoped"));
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
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            return Task.FromResult(new TenantDbContext(options));
        }
    }

    private sealed class StubImportNormalizationService : IImportNormalizationService
    {
        public IReadOnlyList<string> ValidateRow(Dictionary<string, string?> row, Dictionary<string, string> mappings)
        {
            return Array.Empty<string>();
        }

        public NormalizationResult Normalize(Guid batchId, Guid rawRecordId, Dictionary<string, string?> row, Dictionary<string, string> mappings)
        {
            throw new NotImplementedException();
        }
    }

    private sealed class StubAuditLogger : IAuditLogger
    {
        public Task LogAsync(Guid? userId, string action, string? entity = null, string? entityId = null, string? metadata = null)
        {
            return Task.CompletedTask;
        }
    }
}
