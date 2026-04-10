using System.Text.Json;
using finrecon360_backend.Authorization;
using finrecon360_backend.Data;
using finrecon360_backend.Dtos.Admin;
using finrecon360_backend.Models;
using finrecon360_backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace finrecon360_backend.Controllers.Admin
{
    [ApiController]
    [Route("api/admin/import-architecture")]
    [Authorize]
    [EnableRateLimiting("admin")]
    public class AdminImportArchitectureController : ControllerBase
    {
        private readonly AppDbContext _dbContext;
        private readonly ITenantContext _tenantContext;
        private readonly ITenantDbContextFactory _tenantDbContextFactory;
        private readonly IUserContext _userContext;

        public AdminImportArchitectureController(
            AppDbContext dbContext,
            ITenantContext tenantContext,
            ITenantDbContextFactory tenantDbContextFactory,
            IUserContext userContext)
        {
            _dbContext = dbContext;
            _tenantContext = tenantContext;
            _tenantDbContextFactory = tenantDbContextFactory;
            _userContext = userContext;
        }

        [HttpGet("overview")]
        [RequirePermission("ADMIN.IMPORT_ARCHITECTURE.VIEW")]
        public async Task<ActionResult<ImportArchitectureOverviewDto>> GetOverview()
        {
            var auth = await AuthorizeTenantAdminAsync();
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            var totalImportBatches = await tenantDb.ImportBatches.CountAsync();
            var totalRawRecords = await tenantDb.ImportedRawRecords.CountAsync();
            var totalNormalizedRecords = await tenantDb.ImportedNormalizedRecords.CountAsync();
            var activeMappingTemplates = await tenantDb.ImportMappingTemplates.CountAsync(x => x.IsActive);
            var latestImportAt = await tenantDb.ImportBatches
                .OrderByDescending(x => x.ImportedAt)
                .Select(x => (DateTime?)x.ImportedAt)
                .FirstOrDefaultAsync();

            return Ok(new ImportArchitectureOverviewDto(
                totalImportBatches,
                totalRawRecords,
                totalNormalizedRecords,
                activeMappingTemplates,
                latestImportAt,
                BuildCanonicalSchema()));
        }

        [HttpGet("canonical-schema")]
        [RequirePermission("ADMIN.IMPORT_ARCHITECTURE.VIEW")]
        public ActionResult<CanonicalSchemaDto> GetCanonicalSchema()
        {
            return Ok(BuildCanonicalSchema());
        }

        [HttpGet("mapping-templates")]
        [RequirePermission("ADMIN.IMPORT_ARCHITECTURE.VIEW")]
        public async Task<ActionResult<IReadOnlyList<ImportMappingTemplateDto>>> GetMappingTemplates([FromQuery] string? sourceType = null)
        {
            var auth = await AuthorizeTenantAdminAsync();
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            var query = tenantDb.ImportMappingTemplates.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(sourceType))
            {
                var sourceTypeValue = sourceType.Trim();
                query = query.Where(x => x.SourceType == sourceTypeValue);
            }

            var templates = await query
                .OrderByDescending(x => x.UpdatedAt ?? x.CreatedAt)
                .ThenBy(x => x.Name)
                .Select(x => new ImportMappingTemplateDto(
                    x.ImportMappingTemplateId,
                    x.Name,
                    x.SourceType,
                    x.CanonicalSchemaVersion,
                    x.Version,
                    x.IsActive,
                    x.MappingJson,
                    x.CreatedAt,
                    x.UpdatedAt))
                .ToListAsync();

            return Ok(templates);
        }

        [HttpPost("mapping-templates")]
        [RequirePermission("ADMIN.IMPORT_ARCHITECTURE.MANAGE")]
        public async Task<ActionResult<ImportMappingTemplateDto>> CreateMappingTemplate([FromBody] ImportMappingTemplateCreateRequest request)
        {
            var auth = await AuthorizeTenantAdminAsync();
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            var name = request.Name.Trim();
            var sourceType = request.SourceType.Trim();
            var schemaVersion = request.CanonicalSchemaVersion.Trim();

            if (!IsValidJson(request.MappingJson))
            {
                return BadRequest(new { message = "MappingJson must be valid JSON." });
            }

            var duplicate = await tenantDb.ImportMappingTemplates.AnyAsync(x => x.Name == name);
            if (duplicate)
            {
                return Conflict(new { message = "Template name already exists." });
            }

            var now = DateTime.UtcNow;
            var template = new ImportMappingTemplate
            {
                ImportMappingTemplateId = Guid.NewGuid(),
                Name = name,
                SourceType = sourceType,
                CanonicalSchemaVersion = schemaVersion,
                MappingJson = request.MappingJson,
                Version = 1,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now,
                CreatedByUserId = _userContext.UserId
            };

            tenantDb.ImportMappingTemplates.Add(template);
            await tenantDb.SaveChangesAsync();

            var response = new ImportMappingTemplateDto(
                template.ImportMappingTemplateId,
                template.Name,
                template.SourceType,
                template.CanonicalSchemaVersion,
                template.Version,
                template.IsActive,
                template.MappingJson,
                template.CreatedAt,
                template.UpdatedAt);

            return CreatedAtAction(nameof(GetMappingTemplates), new { sourceType = template.SourceType }, response);
        }

        [HttpPut("mapping-templates/{templateId:guid}")]
        [RequirePermission("ADMIN.IMPORT_ARCHITECTURE.MANAGE")]
        public async Task<ActionResult<ImportMappingTemplateDto>> UpdateMappingTemplate(Guid templateId, [FromBody] ImportMappingTemplateUpdateRequest request)
        {
            var auth = await AuthorizeTenantAdminAsync();
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            var template = await tenantDb.ImportMappingTemplates.FirstOrDefaultAsync(x => x.ImportMappingTemplateId == templateId);
            if (template is null)
            {
                return NotFound();
            }

            if (!IsValidJson(request.MappingJson))
            {
                return BadRequest(new { message = "MappingJson must be valid JSON." });
            }

            var newName = request.Name.Trim();
            var duplicate = await tenantDb.ImportMappingTemplates.AnyAsync(x => x.ImportMappingTemplateId != templateId && x.Name == newName);
            if (duplicate)
            {
                return Conflict(new { message = "Template name already exists." });
            }

            template.Name = newName;
            template.SourceType = request.SourceType.Trim();
            template.CanonicalSchemaVersion = request.CanonicalSchemaVersion.Trim();
            template.MappingJson = request.MappingJson;
            template.IsActive = request.IsActive;
            template.Version += 1;
            template.UpdatedAt = DateTime.UtcNow;

            await tenantDb.SaveChangesAsync();

            return Ok(new ImportMappingTemplateDto(
                template.ImportMappingTemplateId,
                template.Name,
                template.SourceType,
                template.CanonicalSchemaVersion,
                template.Version,
                template.IsActive,
                template.MappingJson,
                template.CreatedAt,
                template.UpdatedAt));
        }

        [HttpDelete("mapping-templates/{templateId:guid}")]
        [RequirePermission("ADMIN.IMPORT_ARCHITECTURE.MANAGE")]
        public async Task<IActionResult> DeactivateMappingTemplate(Guid templateId)
        {
            var auth = await AuthorizeTenantAdminAsync();
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            var template = await tenantDb.ImportMappingTemplates.FirstOrDefaultAsync(x => x.ImportMappingTemplateId == templateId);
            if (template is null)
            {
                return NotFound();
            }

            if (!template.IsActive)
            {
                return NoContent();
            }

            template.IsActive = false;
            template.UpdatedAt = DateTime.UtcNow;
            await tenantDb.SaveChangesAsync();
            return NoContent();
        }

        [HttpPost("batches")]
        [RequirePermission("ADMIN.IMPORT_ARCHITECTURE.MANAGE")]
        public async Task<ActionResult<ImportBatchDto>> CreateImportBatch([FromBody] CreateImportBatchRequest request)
        {
            var auth = await AuthorizeTenantAdminAsync();
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            var now = DateTime.UtcNow;
            var batch = new ImportBatch
            {
                ImportBatchId = Guid.NewGuid(),
                SourceType = request.SourceType.Trim(),
                Status = request.Status.Trim(),
                ImportedAt = now,
                UploadedByUserId = _userContext.UserId,
                OriginalFileName = string.IsNullOrWhiteSpace(request.OriginalFileName) ? null : request.OriginalFileName.Trim(),
                ErrorMessage = string.IsNullOrWhiteSpace(request.ErrorMessage) ? null : request.ErrorMessage.Trim(),
                RawRecordCount = 0,
                NormalizedRecordCount = 0
            };

            tenantDb.ImportBatches.Add(batch);
            await tenantDb.SaveChangesAsync();

            return CreatedAtAction(nameof(GetImportBatch), new { batchId = batch.ImportBatchId }, ToDto(batch));
        }

        [HttpGet("batches/{batchId:guid}")]
        [RequirePermission("ADMIN.IMPORT_ARCHITECTURE.VIEW")]
        public async Task<ActionResult<ImportBatchDto>> GetImportBatch(Guid batchId)
        {
            var auth = await AuthorizeTenantAdminAsync();
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            var batch = await tenantDb.ImportBatches.AsNoTracking().FirstOrDefaultAsync(x => x.ImportBatchId == batchId);
            if (batch is null)
            {
                return NotFound();
            }

            return Ok(ToDto(batch));
        }

        [HttpPost("batches/{batchId:guid}/raw-records")]
        [RequirePermission("ADMIN.IMPORT_ARCHITECTURE.MANAGE")]
        public async Task<IActionResult> AddRawRecord(Guid batchId, [FromBody] ImportRawRecordRequest request)
        {
            var auth = await AuthorizeTenantAdminAsync();
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            var batch = await tenantDb.ImportBatches.FirstOrDefaultAsync(x => x.ImportBatchId == batchId);
            if (batch is null)
            {
                return NotFound();
            }

            var rawRecord = new ImportedRawRecord
            {
                ImportedRawRecordId = Guid.NewGuid(),
                ImportBatchId = batchId,
                RowNumber = request.RowNumber,
                SourcePayloadJson = request.SourcePayload.GetRawText(),
                NormalizationStatus = request.NormalizationStatus.Trim(),
                NormalizationErrors = string.IsNullOrWhiteSpace(request.NormalizationErrors) ? null : request.NormalizationErrors.Trim(),
                CreatedAt = DateTime.UtcNow
            };

            tenantDb.ImportedRawRecords.Add(rawRecord);
            batch.RawRecordCount += 1;
            await tenantDb.SaveChangesAsync();

            return NoContent();
        }

        [HttpPost("batches/{batchId:guid}/normalized-records")]
        [RequirePermission("ADMIN.IMPORT_ARCHITECTURE.MANAGE")]
        public async Task<IActionResult> AddNormalizedRecord(Guid batchId, [FromBody] ImportNormalizedRecordRequest request)
        {
            var auth = await AuthorizeTenantAdminAsync();
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            var batch = await tenantDb.ImportBatches.FirstOrDefaultAsync(x => x.ImportBatchId == batchId);
            if (batch is null)
            {
                return NotFound();
            }

            if (request.SourceRawRecordId.HasValue)
            {
                var sourceRawExists = await tenantDb.ImportedRawRecords.AnyAsync(x => x.ImportedRawRecordId == request.SourceRawRecordId.Value && x.ImportBatchId == batchId);
                if (!sourceRawExists)
                {
                    return BadRequest(new { message = "SourceRawRecordId does not belong to the import batch." });
                }
            }

            var normalizedRecord = new ImportedNormalizedRecord
            {
                ImportedNormalizedRecordId = Guid.NewGuid(),
                ImportBatchId = batchId,
                SourceRawRecordId = request.SourceRawRecordId,
                TransactionDate = request.TransactionDate,
                PostingDate = request.PostingDate,
                ReferenceNumber = string.IsNullOrWhiteSpace(request.ReferenceNumber) ? null : request.ReferenceNumber.Trim(),
                Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
                AccountCode = string.IsNullOrWhiteSpace(request.AccountCode) ? null : request.AccountCode.Trim(),
                AccountName = string.IsNullOrWhiteSpace(request.AccountName) ? null : request.AccountName.Trim(),
                DebitAmount = request.DebitAmount,
                CreditAmount = request.CreditAmount,
                NetAmount = request.NetAmount,
                Currency = request.Currency.Trim().ToUpperInvariant(),
                CreatedAt = DateTime.UtcNow
            };

            tenantDb.ImportedNormalizedRecords.Add(normalizedRecord);
            batch.NormalizedRecordCount += 1;
            await tenantDb.SaveChangesAsync();

            return NoContent();
        }

        private static CanonicalSchemaDto BuildCanonicalSchema() =>
            new(
                "v1",
                new List<CanonicalFieldDto>
                {
                    new("TransactionDate", "date", true, "Primary business transaction date."),
                    new("PostingDate", "date", false, "Ledger posting date when available."),
                    new("ReferenceNumber", "string", false, "External document or reference number."),
                    new("Description", "string", false, "Narration or description from source."),
                    new("AccountCode", "string", false, "Chart-of-accounts code."),
                    new("AccountName", "string", false, "Chart-of-accounts display name."),
                    new("DebitAmount", "decimal(18,2)", true, "Debit amount in transaction currency."),
                    new("CreditAmount", "decimal(18,2)", true, "Credit amount in transaction currency."),
                    new("NetAmount", "decimal(18,2)", true, "Net amount (Debit - Credit or source-provided net)."),
                    new("Currency", "char(3)", true, "ISO 4217 currency code.")
                });

        private static ImportBatchDto ToDto(ImportBatch batch) =>
            new(
                batch.ImportBatchId,
                batch.SourceType,
                batch.Status,
                batch.ImportedAt,
                batch.UploadedByUserId,
                batch.OriginalFileName,
                batch.RawRecordCount,
                batch.NormalizedRecordCount,
                batch.ErrorMessage);

        private static bool IsValidJson(string value)
        {
            try
            {
                _ = JsonDocument.Parse(value);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private async Task<(TenantDbContext? Db, ActionResult? Error)> AuthorizeTenantAdminAsync()
        {
            if (_userContext.UserId is not { } userId) return (null, Unauthorized());

            var tenant = await _tenantContext.ResolveAsync();
            if (tenant == null) return (null, Forbid());

            var isTenantMember = await _dbContext.TenantUsers.AsNoTracking()
                .AnyAsync(tu => tu.TenantId == tenant.TenantId && tu.UserId == userId);
            if (!isTenantMember) return (null, Forbid());

            var tenantDb = await _tenantDbContextFactory.CreateAsync(tenant.TenantId);
            var isActiveInTenant = await tenantDb.TenantUsers.AsNoTracking().AnyAsync(tu => tu.UserId == userId && tu.IsActive);
            if (!isActiveInTenant) return (null, Forbid());
            return (tenantDb, null);
        }
    }
}
