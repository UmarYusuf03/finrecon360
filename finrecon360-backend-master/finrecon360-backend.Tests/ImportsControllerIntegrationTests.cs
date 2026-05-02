using System.Text;
using ClosedXML.Excel;
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

public class ImportsControllerIntegrationTests
{
    [Fact]
    public async Task Csv_pipeline_upload_parse_validate_commit_succeeds_and_links_mapping_template()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var appDb = CreateAppDb();
        var tenantDbName = $"TenantImportDbCsv-{Guid.NewGuid()}";

        await SeedAppAndTenantAsync(appDb, tenantDbName, tenantId, userId, TenantUserRole.TenantAdmin);

        var controller = CreateController(appDb, tenantDbName, tenantId, userId);

        var csvContent = string.Join('\n',
            "TransactionDate,PostingDate,ReferenceNumber,Description,AccountCode,AccountName,DebitAmount,CreditAmount,NetAmount,Currency",
            "2026-04-10,2026-04-10,REF-001,Payment,ACC-100,Cash,100.00,0.00,100.00,LKR");

        var uploadResult = await controller.Upload(CreateFormFile("sample.csv", Encoding.UTF8.GetBytes(csvContent)), "CSV");
        var uploadOk = Assert.IsType<OkObjectResult>(uploadResult.Result);
        var uploadDto = Assert.IsType<ImportUploadResponseDto>(uploadOk.Value);

        var parseResult = await controller.Parse(uploadDto.Id);
        var parseOk = Assert.IsType<OkObjectResult>(parseResult.Result);
        var parseDto = Assert.IsType<ImportParseResponseDto>(parseOk.Value);
        Assert.Equal(1, parseDto.ParsedRowCount);

        var mappingResult = await controller.SaveMapping(uploadDto.Id, new SaveImportMappingRequest
        {
            CanonicalSchemaVersion = "v1",
            FieldMappings = BuildIdentityMapping()
        });
        Assert.IsType<OkObjectResult>(mappingResult.Result);

        var validateResult = await controller.Validate(uploadDto.Id);
        var validateOk = Assert.IsType<OkObjectResult>(validateResult.Result);
        var validateDto = Assert.IsType<ImportValidateResponseDto>(validateOk.Value);
        Assert.Equal(1, validateDto.ValidRows);
        Assert.Equal(0, validateDto.InvalidRows);

        var commitResult = await controller.Commit(uploadDto.Id);
        var commitOk = Assert.IsType<OkObjectResult>(commitResult.Result);
        var commitDto = Assert.IsType<ImportCommitResponseDto>(commitOk.Value);
        Assert.Equal(1, commitDto.NormalizedCount);

        await using var verifyTenantDb = CreateTenantDb(tenantDbName);
        var batch = await verifyTenantDb.ImportBatches.AsNoTracking().FirstAsync(x => x.ImportBatchId == uploadDto.Id);
        Assert.NotNull(batch.MappingTemplateId);
        Assert.Equal("COMMITTED", batch.Status);
        Assert.Equal(1, await verifyTenantDb.ImportedNormalizedRecords.CountAsync(x => x.ImportBatchId == uploadDto.Id));
    }

    [Fact]
    public async Task Xlsx_pipeline_upload_parse_validate_commit_succeeds_and_links_mapping_template()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var appDb = CreateAppDb();
        var tenantDbName = $"TenantImportDbXlsx-{Guid.NewGuid()}";

        await SeedAppAndTenantAsync(appDb, tenantDbName, tenantId, userId, TenantUserRole.TenantAdmin);

        var controller = CreateController(appDb, tenantDbName, tenantId, userId);

        var xlsxBytes = BuildXlsxBytes();

        var uploadResult = await controller.Upload(CreateFormFile("sample.xlsx", xlsxBytes), "XLSX");
        var uploadOk = Assert.IsType<OkObjectResult>(uploadResult.Result);
        var uploadDto = Assert.IsType<ImportUploadResponseDto>(uploadOk.Value);

        var parseResult = await controller.Parse(uploadDto.Id);
        var parseOk = Assert.IsType<OkObjectResult>(parseResult.Result);
        var parseDto = Assert.IsType<ImportParseResponseDto>(parseOk.Value);
        Assert.Equal(1, parseDto.ParsedRowCount);

        var mappingResult = await controller.SaveMapping(uploadDto.Id, new SaveImportMappingRequest
        {
            CanonicalSchemaVersion = "v1",
            FieldMappings = BuildIdentityMapping()
        });
        Assert.IsType<OkObjectResult>(mappingResult.Result);

        var validateResult = await controller.Validate(uploadDto.Id);
        var validateOk = Assert.IsType<OkObjectResult>(validateResult.Result);
        var validateDto = Assert.IsType<ImportValidateResponseDto>(validateOk.Value);
        Assert.Equal(1, validateDto.ValidRows);
        Assert.Equal(0, validateDto.InvalidRows);

        var commitResult = await controller.Commit(uploadDto.Id);
        var commitOk = Assert.IsType<OkObjectResult>(commitResult.Result);
        var commitDto = Assert.IsType<ImportCommitResponseDto>(commitOk.Value);
        Assert.Equal(1, commitDto.NormalizedCount);

        await using var verifyTenantDb = CreateTenantDb(tenantDbName);
        var batch = await verifyTenantDb.ImportBatches.AsNoTracking().FirstAsync(x => x.ImportBatchId == uploadDto.Id);
        Assert.NotNull(batch.MappingTemplateId);
        Assert.Equal("COMMITTED", batch.Status);
        Assert.Equal(1, await verifyTenantDb.ImportedNormalizedRecords.CountAsync(x => x.ImportBatchId == uploadDto.Id));
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
            new ReconciliationOrchestrator(),
            new StubAuditLogger());
    }

    private static AppDbContext CreateAppDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"AppDb-Imports-{Guid.NewGuid()}")
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

    private static async Task SeedAppAndTenantAsync(AppDbContext appDb, string tenantDbName, Guid tenantId, Guid userId, TenantUserRole role)
    {
        var now = DateTime.UtcNow;

        appDb.Tenants.Add(new Tenant
        {
            TenantId = tenantId,
            Name = "Tenant Imports",
            Status = TenantStatus.Active,
            CreatedAt = now
        });

        appDb.Users.Add(new User
        {
            UserId = userId,
            Email = "tenantadmin@test.local",
            DisplayName = "Tenant Admin",
            FirstName = "Tenant",
            LastName = "Admin",
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
            Role = role,
            CreatedAt = now
        });

        await appDb.SaveChangesAsync();

        await using var tenantDb = CreateTenantDb(tenantDbName);

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
            UserId = userId,
            Email = "tenantadmin@test.local",
            DisplayName = "Tenant Admin",
            Role = role.ToString(),
            Status = UserStatus.Active.ToString(),
            IsActive = true,
            CreatedAt = now
        });

        tenantDb.UserRoles.Add(new TenantUserRoleAssignment
        {
            UserId = userId,
            RoleId = role == TenantUserRole.TenantAdmin ? adminRoleId : userRoleId,
            AssignedAt = now
        });

        await tenantDb.SaveChangesAsync();
    }

    private static IFormFile CreateFormFile(string fileName, byte[] content)
    {
        var stream = new MemoryStream(content);
        return new FormFile(stream, 0, content.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
                ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
                : "text/csv"
        };
    }

    private static byte[] BuildXlsxBytes()
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Sheet1");

        ws.Cell(1, 1).Value = "TransactionDate";
        ws.Cell(1, 2).Value = "PostingDate";
        ws.Cell(1, 3).Value = "ReferenceNumber";
        ws.Cell(1, 4).Value = "Description";
        ws.Cell(1, 5).Value = "AccountCode";
        ws.Cell(1, 6).Value = "AccountName";
        ws.Cell(1, 7).Value = "DebitAmount";
        ws.Cell(1, 8).Value = "CreditAmount";
        ws.Cell(1, 9).Value = "NetAmount";
        ws.Cell(1, 10).Value = "Currency";

        ws.Cell(2, 1).Value = "2026-04-10";
        ws.Cell(2, 2).Value = "2026-04-10";
        ws.Cell(2, 3).Value = "REF-XL-001";
        ws.Cell(2, 4).Value = "Excel Payment";
        ws.Cell(2, 5).Value = "ACC-200";
        ws.Cell(2, 6).Value = "Bank";
        ws.Cell(2, 7).Value = 250.50;
        ws.Cell(2, 8).Value = 0;
        ws.Cell(2, 9).Value = 250.50;
        ws.Cell(2, 10).Value = "LKR";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static Dictionary<string, string> BuildIdentityMapping() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["TransactionDate"] = "TransactionDate",
        ["PostingDate"] = "PostingDate",
        ["ReferenceNumber"] = "ReferenceNumber",
        ["Description"] = "Description",
        ["AccountCode"] = "AccountCode",
        ["AccountName"] = "AccountName",
        ["DebitAmount"] = "DebitAmount",
        ["CreditAmount"] = "CreditAmount",
        ["NetAmount"] = "NetAmount",
        ["Currency"] = "Currency"
    };

    private sealed class StubUserContext : IUserContext
    {
        public StubUserContext(Guid userId)
        {
            UserId = userId;
        }

        public Guid? UserId { get; }
        public string? Email => "tenantadmin@test.local";
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
            return Task.FromResult<TenantResolution?>(new TenantResolution(_tenantId, TenantStatus.Active, "Tenant Imports"));
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
            var normalized = new ImportedNormalizedRecord
            {
                ImportedNormalizedRecordId = Guid.NewGuid(),
                ImportBatchId = batchId,
                SourceRawRecordId = rawRecordId,
                TransactionDate = DateTime.UtcNow,
                PostingDate = DateTime.UtcNow,
                ReferenceNumber = "REF",
                Description = "Test",
                AccountCode = "ACC",
                AccountName = "Account",
                DebitAmount = 1m,
                CreditAmount = 0m,
                NetAmount = 1m,
                Currency = "LKR",
                CreatedAt = DateTime.UtcNow
            };

            return new NormalizationResult(normalized, Array.Empty<string>());
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
