using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using finrecon360_backend.Controllers;
using finrecon360_backend.Data;
using finrecon360_backend.Dtos.Imports;
using finrecon360_backend.Models;
using finrecon360_backend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace finrecon360_backend.Tests;

public class ImportsHistoryControllerTests
{
    [Fact]
    public async Task GetHistory_filters_by_status()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var appDb = CreateAppDb();
        var tenantDbName = $"TenantImportHistory-{Guid.NewGuid()}";

        await SeedAppDbAsync(appDb, tenantId, userId);
        await SeedTenantDbAsync(tenantDbName, userId);

        var controller = CreateController(appDb, tenantDbName, tenantId, userId);

        var result = await controller.GetHistory(status: "COMMITTED");
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<ImportHistoryResponseDto>(ok.Value);

        Assert.Single(dto.Items);
        Assert.Equal("COMMITTED", dto.Items.Single().Status);
    }

    [Fact]
    public async Task GetHistory_filters_by_search_term()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var appDb = CreateAppDb();
        var tenantDbName = $"TenantImportHistory-{Guid.NewGuid()}";

        await SeedAppDbAsync(appDb, tenantId, userId);
        await SeedTenantDbAsync(tenantDbName, userId);

        var controller = CreateController(appDb, tenantDbName, tenantId, userId);

        var result = await controller.GetHistory(search: "bank");
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<ImportHistoryResponseDto>(ok.Value);

        Assert.Single(dto.Items);
        Assert.Contains("bank", dto.Items.Single().SourceType, StringComparison.OrdinalIgnoreCase);
    }

    private static ImportsController CreateController(
        AppDbContext appDb,
        string tenantDbName,
        Guid tenantId,
        Guid userId)
    {
        return new ImportsController(
            new StubTenantContext(tenantId),
            new InMemoryTenantDbContextFactory(tenantDbName),
            new StubUserContext(userId),
            new ImportFileParser(),
            new StubImportNormalizationService(),
            new ReconciliationOrchestrator(),
            new ReconciliationExecutionService(),
            new StubAuditLogger());
    }

    private static AppDbContext CreateAppDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"AppDb-ImportHistory-{Guid.NewGuid()}")
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
            Email = "user@test.local",
            DisplayName = "User",
            FirstName = "User",
            LastName = "Test",
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

    private static async Task SeedTenantDbAsync(string tenantDbName, Guid userId)
    {
        await using var tenantDb = CreateTenantDb(tenantDbName);
        var now = DateTime.UtcNow;

        tenantDb.TenantUsers.Add(new TenantScopedUser
        {
            TenantUserId = Guid.NewGuid(),
            UserId = userId,
            Email = "user@test.local",
            DisplayName = "User",
            Role = TenantUserRole.TenantAdmin.ToString(),
            Status = UserStatus.Active.ToString(),
            IsActive = true,
            CreatedAt = now
        });

        tenantDb.ImportBatches.AddRange(
            new ImportBatch
            {
                ImportBatchId = Guid.NewGuid(),
                SourceType = "BANK",
                Status = "COMMITTED",
                ImportedAt = now,
                OriginalFileName = "bank.csv",
                RawRecordCount = 1,
                NormalizedRecordCount = 1
            },
            new ImportBatch
            {
                ImportBatchId = Guid.NewGuid(),
                SourceType = "ERP",
                Status = "RECEIVED",
                ImportedAt = now.AddMinutes(-10),
                OriginalFileName = "erp.csv",
                RawRecordCount = 0,
                NormalizedRecordCount = 0
            }
        );

        await tenantDb.SaveChangesAsync();
    }

    private sealed class StubUserContext : IUserContext
    {
        public StubUserContext(Guid userId)
        {
            UserId = userId;
        }

        public Guid? UserId { get; }
        public string? Email => "user@test.local";
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
