using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using finrecon360_backend.Controllers.Admin;
using finrecon360_backend.Data;
using finrecon360_backend.Dtos.Admin;
using finrecon360_backend.Models;
using finrecon360_backend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace finrecon360_backend.Tests;

public class AdminImportArchitectureControllerTests
{
    [Fact]
    public async Task GetOverview_returns_counts_and_schema()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var appDb = CreateAppDb();
        var tenantDbName = $"TenantArchDb-{Guid.NewGuid()}";

        await SeedAppDbAsync(appDb, tenantId, userId);
        await SeedTenantDbForOverviewAsync(tenantDbName, userId);

        var controller = CreateController(appDb, tenantDbName, tenantId, userId);

        var result = await controller.GetOverview();
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<ImportArchitectureOverviewDto>(ok.Value);

        Assert.Equal(2, dto.TotalImportBatches);
        Assert.Equal(3, dto.TotalRawRecords);
        Assert.Equal(1, dto.TotalNormalizedRecords);
        Assert.Equal(1, dto.ActiveMappingTemplates);
        Assert.NotNull(dto.CanonicalSchema);
    }

    [Fact]
    public async Task CreateMappingTemplate_rejects_invalid_json()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var appDb = CreateAppDb();
        var tenantDbName = $"TenantArchDb-{Guid.NewGuid()}";

        await SeedAppDbAsync(appDb, tenantId, userId);
        await SeedTenantDbEmptyAsync(tenantDbName, userId);

        var controller = CreateController(appDb, tenantDbName, tenantId, userId);

        var result = await controller.CreateMappingTemplate(new ImportMappingTemplateCreateRequest
        {
            Name = "Bad",
            SourceType = "CSV",
            CanonicalSchemaVersion = "v1",
            MappingJson = "not-json"
        });

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task CreateMappingTemplate_creates_template()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var appDb = CreateAppDb();
        var tenantDbName = $"TenantArchDb-{Guid.NewGuid()}";

        await SeedAppDbAsync(appDb, tenantId, userId);
        await SeedTenantDbEmptyAsync(tenantDbName, userId);

        var controller = CreateController(appDb, tenantDbName, tenantId, userId);

        var result = await controller.CreateMappingTemplate(new ImportMappingTemplateCreateRequest
        {
            Name = "Template A",
            SourceType = "CSV",
            CanonicalSchemaVersion = "v1",
            MappingJson = "{\"TransactionDate\":\"Date\"}"
        });

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var dto = Assert.IsType<ImportMappingTemplateDto>(created.Value);
        Assert.Equal("Template A", dto.Name);

        await using var tenantDb = CreateTenantDb(tenantDbName);
        var stored = await tenantDb.ImportMappingTemplates.FirstOrDefaultAsync(x => x.ImportMappingTemplateId == dto.Id);
        Assert.NotNull(stored);
    }

    private static AdminImportArchitectureController CreateController(
        AppDbContext appDb,
        string tenantDbName,
        Guid tenantId,
        Guid userId)
    {
        return new AdminImportArchitectureController(
            appDb,
            new StubTenantContext(tenantId),
            new InMemoryTenantDbContextFactory(tenantDbName),
            new StubUserContext(userId),
            new StubAuditLogger());
    }

    private static AppDbContext CreateAppDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"AppDb-AdminImportArch-{Guid.NewGuid()}")
            .Options;
        return new AppDbContext(options);
    }

    private static TenantDbContext CreateTenantDb(string tenantDbName)
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseInMemoryDatabase(tenantDbName)
            .Options;
        return new TenantDbContext(options);
    }

    private static async Task SeedAppDbAsync(AppDbContext appDb, Guid tenantId, Guid userId)
    {
        var now = DateTime.UtcNow;
        appDb.Tenants.Add(new Tenant
        {
            TenantId = tenantId,
            Name = "Tenant",
            Status = TenantStatus.Active,
            CreatedAt = now
        });

        appDb.Users.Add(new User
        {
            UserId = userId,
            Email = "admin@test.local",
            DisplayName = "Admin",
            FirstName = "Admin",
            LastName = "User",
            Country = "LK",
            Gender = "NA",
            PasswordHash = "hash",
            CreatedAt = now,
            IsActive = true,
            Status = UserStatus.Active
        });

        appDb.TenantUsers.Add(new TenantUser
        {
            TenantUserId = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            Role = TenantUserRole.TenantAdmin,
            CreatedAt = now
        });

        await appDb.SaveChangesAsync();
    }

    private static async Task SeedTenantDbForOverviewAsync(string tenantDbName, Guid userId)
    {
        await using var tenantDb = CreateTenantDb(tenantDbName);
        var now = DateTime.UtcNow;

        tenantDb.TenantUsers.Add(new TenantScopedUser
        {
            TenantUserId = Guid.NewGuid(),
            UserId = userId,
            Email = "admin@test.local",
            DisplayName = "Admin",
            Role = TenantUserRole.TenantAdmin.ToString(),
            Status = UserStatus.Active.ToString(),
            IsActive = true,
            CreatedAt = now
        });

        var batchA = new ImportBatch
        {
            ImportBatchId = Guid.NewGuid(),
            SourceType = "CSV",
            Status = "RECEIVED",
            ImportedAt = now.AddMinutes(-5),
            RawRecordCount = 2,
            NormalizedRecordCount = 1
        };
        var batchB = new ImportBatch
        {
            ImportBatchId = Guid.NewGuid(),
            SourceType = "CSV",
            Status = "RECEIVED",
            ImportedAt = now,
            RawRecordCount = 1,
            NormalizedRecordCount = 0
        };

        tenantDb.ImportBatches.AddRange(batchA, batchB);
        tenantDb.ImportedRawRecords.AddRange(
            new ImportedRawRecord { ImportedRawRecordId = Guid.NewGuid(), ImportBatchId = batchA.ImportBatchId, CreatedAt = now },
            new ImportedRawRecord { ImportedRawRecordId = Guid.NewGuid(), ImportBatchId = batchA.ImportBatchId, CreatedAt = now },
            new ImportedRawRecord { ImportedRawRecordId = Guid.NewGuid(), ImportBatchId = batchB.ImportBatchId, CreatedAt = now }
        );
        tenantDb.ImportedNormalizedRecords.Add(new ImportedNormalizedRecord
        {
            ImportedNormalizedRecordId = Guid.NewGuid(),
            ImportBatchId = batchA.ImportBatchId,
            TransactionDate = now,
            PostingDate = now,
            ReferenceNumber = "REF",
            Description = "Desc",
            AccountCode = "ACC",
            AccountName = "Name",
            DebitAmount = 1m,
            CreditAmount = 0m,
            NetAmount = 1m,
            Currency = "LKR",
            CreatedAt = now
        });
        tenantDb.ImportMappingTemplates.AddRange(
            new ImportMappingTemplate
            {
                ImportMappingTemplateId = Guid.NewGuid(),
                Name = "Active",
                SourceType = "CSV",
                CanonicalSchemaVersion = "v1",
                MappingJson = "{}",
                Version = 1,
                IsActive = true,
                CreatedAt = now
            },
            new ImportMappingTemplate
            {
                ImportMappingTemplateId = Guid.NewGuid(),
                Name = "Inactive",
                SourceType = "CSV",
                CanonicalSchemaVersion = "v1",
                MappingJson = "{}",
                Version = 1,
                IsActive = false,
                CreatedAt = now
            }
        );

        await tenantDb.SaveChangesAsync();
    }

    private static async Task SeedTenantDbEmptyAsync(string tenantDbName, Guid userId)
    {
        await using var tenantDb = CreateTenantDb(tenantDbName);
        tenantDb.TenantUsers.Add(new TenantScopedUser
        {
            TenantUserId = Guid.NewGuid(),
            UserId = userId,
            Email = "admin@test.local",
            DisplayName = "Admin",
            Role = TenantUserRole.TenantAdmin.ToString(),
            Status = UserStatus.Active.ToString(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await tenantDb.SaveChangesAsync();
    }

    private sealed class StubUserContext : IUserContext
    {
        public StubUserContext(Guid userId)
        {
            UserId = userId;
        }

        public Guid? UserId { get; }
        public string? Email => "admin@test.local";
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
            return Task.FromResult<TenantResolution?>(new TenantResolution(_tenantId, TenantStatus.Active, "Tenant"));
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

    private sealed class StubAuditLogger : IAuditLogger
    {
        public Task LogAsync(Guid? userId, string action, string? entity = null, string? entityId = null, string? metadata = null)
        {
            return Task.CompletedTask;
        }
    }
}
