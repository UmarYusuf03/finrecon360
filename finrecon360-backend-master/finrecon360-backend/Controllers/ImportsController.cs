using System.Text.Json;
using finrecon360_backend.Authorization;
using finrecon360_backend.Data;
using finrecon360_backend.Dtos.Imports;
using finrecon360_backend.Models;
using finrecon360_backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace finrecon360_backend.Controllers
{
    [ApiController]
    [Route("api/imports")]
    [Authorize]
    [EnableRateLimiting("me")]
    public class ImportsController : ControllerBase
    {
        private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".csv", ".xlsx"
        };

        /// <summary>
        /// WHY: The reconciliation engine routes entirely on SourceType.
        /// Any unknown value causes a silent no-op in ReconciliationExecutionService.
        /// Enforcing at the boundary prevents silent reconciliation failures.
        /// </summary>
        private static readonly HashSet<string> ValidSourceTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "ERP", "GATEWAY", "BANK", "POS"
        };


        private readonly ITenantContext _tenantContext;
        private readonly ITenantDbContextFactory _tenantDbContextFactory;
        private readonly IUserContext _userContext;
        private readonly IImportFileParser _importFileParser;
        private readonly IImportNormalizationService _normalizationService;
        private readonly IReconciliationOrchestrator _reconciliationOrchestrator;
        private readonly IReconciliationExecutionService _reconciliationExecutionService;
        private readonly IAuditLogger _auditLogger;

        public ImportsController(
            ITenantContext tenantContext,
            ITenantDbContextFactory tenantDbContextFactory,
            IUserContext userContext,
            IImportFileParser importFileParser,
            IImportNormalizationService normalizationService,
            IReconciliationOrchestrator reconciliationOrchestrator,
            IReconciliationExecutionService reconciliationExecutionService,
            IAuditLogger auditLogger)
        {
            // Wire required services for import lifecycle operations.
            _tenantContext = tenantContext;
            _tenantDbContextFactory = tenantDbContextFactory;
            _userContext = userContext;
            _importFileParser = importFileParser;
            _normalizationService = normalizationService;
            _reconciliationOrchestrator = reconciliationOrchestrator;
            _reconciliationExecutionService = reconciliationExecutionService;
            _auditLogger = auditLogger;
        }

        [HttpPost]
        [RequestSizeLimit(25 * 1024 * 1024)]
        // WHY: Upload requires CREATE — a MANAGER can upload without needing COMMIT or DELETE.
        [RequirePermission("ADMIN.IMPORTS.CREATE")]
        public async Task<ActionResult<ImportUploadResponseDto>> Upload([FromForm] IFormFile file, [FromForm] string? sourceType = null)
        {
            var auth = await AuthorizeTenantUserAsync();
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            if (file == null || file.Length == 0)
            {
                return BadRequest(new { message = "A CSV or XLSX file is required." });
            }

            var extension = Path.GetExtension(file.FileName);
            if (!SupportedExtensions.Contains(extension))
            {
                return BadRequest(new { message = "Unsupported file type. Only CSV and XLSX are supported." });
            }

            var batchId = Guid.NewGuid();
            var now = DateTime.UtcNow;
            var normalizedSourceType = string.IsNullOrWhiteSpace(sourceType)
                ? null
                : sourceType.Trim().ToUpperInvariant();

            if (normalizedSourceType == null)
            {
                return BadRequest(new { message = "SourceType is required. Must be one of: ERP, GATEWAY, BANK, POS." });
            }

            if (!ValidSourceTypes.Contains(normalizedSourceType))
            {
                return BadRequest(new { message = $"Invalid SourceType '{normalizedSourceType}'. Must be one of: ERP, GATEWAY, BANK, POS." });
            }

            // WHY: Source-type scope check — CASHIER may only upload POS files.
            // PermissionHandler already passed ADMIN.IMPORTS.CREATE; now verify the scoped sub-permission.
            var userPerms = await GetUserPermissionsAsync(tenantDb);
            if (!SourceTypeScope.IsAllowed(userPerms, "IMPORTS", "CREATE", normalizedSourceType))
            {
                return Forbid();
            }

            var batch = new ImportBatch
            {
                ImportBatchId = batchId,
                SourceType = normalizedSourceType,
                Status = "RECEIVED",
                ImportedAt = now,
                UploadedByUserId = _userContext.UserId,
                OriginalFileName = file.FileName,
                RawRecordCount = 0,
                NormalizedRecordCount = 0,
                ErrorMessage = null
            };

            tenantDb.ImportBatches.Add(batch);
            await tenantDb.SaveChangesAsync();

            // Store the raw file under a tenant-specific import directory.
            var tenantImportDir = Path.Combine(
                Directory.GetCurrentDirectory(),
                "App_Data",
                "imports",
                auth.TenantId!.Value.ToString("N"));
            Directory.CreateDirectory(tenantImportDir);

            var storedFilePath = Path.Combine(tenantImportDir, $"{batchId:N}{extension}");
            await using (var stream = System.IO.File.Create(storedFilePath))
            {
                await file.CopyToAsync(stream);
            }

            return Ok(new ImportUploadResponseDto(
                batch.ImportBatchId,
                batch.Status,
                batch.SourceType,
                batch.OriginalFileName ?? file.FileName,
                batch.ImportedAt));
        }

        [HttpGet]
        // WHY: View history is readable by MANAGER and REVIEWER — no mutation needed.
        // Source-type scope is enforced here: a CASHIER with only POS.CREATE only sees POS batches.
        [RequirePermission("ADMIN.IMPORTS.VIEW")]
        public async Task<ActionResult<ImportHistoryResponseDto>> GetHistory(
            [FromQuery] string? search = null,
            [FromQuery] string? status = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            // List import batches with optional filters and paging.
            var auth = await AuthorizeTenantUserAsync();
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 200);

            var query = tenantDb.ImportBatches.AsNoTracking();

            // WHY: Resolve which source types this user may see. null = unrestricted (ADMIN/MANAGER).
            // CASHIER with POS.CREATE will only receive POS rows; other rows are invisible.
            var userPerms = await GetUserPermissionsAsync(tenantDb);
            var allowedTypes = SourceTypeScope.AllowedSourceTypes(userPerms, "IMPORTS", "CREATE")
                           ?? SourceTypeScope.AllowedSourceTypes(userPerms, "IMPORTS", "EDIT");
            if (allowedTypes != null && allowedTypes.Count > 0)
            {
                var typeList = allowedTypes.ToList();
                query = query.Where(x => typeList.Contains(x.SourceType));
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(x =>
                    (x.OriginalFileName != null && x.OriginalFileName.Contains(term)) ||
                    x.SourceType.Contains(term));
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                var statusTerm = status.Trim();
                query = query.Where(x => x.Status == statusTerm);
            }

            var total = await query.CountAsync();
            var users = tenantDb.TenantUsers.AsNoTracking();

            var items = await (from batch in query
                               join user in users on batch.UploadedByUserId equals user.UserId into userGroup
                               from user in userGroup.DefaultIfEmpty()
                               orderby batch.ImportedAt descending
                               select new ImportHistoryItemDto(
                                   batch.ImportBatchId,
                                   batch.SourceType,
                                   batch.Status,
                                   batch.ImportedAt,
                                   batch.OriginalFileName,
                                   batch.RawRecordCount,
                                   batch.NormalizedRecordCount,
                                   batch.ErrorMessage,
                                   batch.UploadedByUserId,
                                   user != null ? user.Email : null,
                                   user != null ? user.DisplayName : null))
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return Ok(new ImportHistoryResponseDto(items, total, page, pageSize));
        }

        [HttpGet("{id:guid}/validation-rows")]
        // WHY: Reading validation rows is a VIEW action — no mutation occurs here.
        [RequirePermission("ADMIN.IMPORTS.VIEW")]
        public async Task<ActionResult<ImportValidationRowsResponseDto>> GetValidationRows(
            Guid id,
            [FromQuery] string? status = null)
        {
            // Return validation status and raw payloads for a batch.
            var auth = await AuthorizeTenantUserAsync();
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            var batch = await tenantDb.ImportBatches.AsNoTracking()
                .FirstOrDefaultAsync(x => x.ImportBatchId == id);
            if (batch == null)
            {
                return NotFound();
            }

            if (!batch.MappingTemplateId.HasValue)
            {
                return BadRequest(new { message = "No mapping saved for this batch." });
            }

            var mappingTemplate = await tenantDb.ImportMappingTemplates.AsNoTracking()
                .FirstOrDefaultAsync(x => x.ImportMappingTemplateId == batch.MappingTemplateId.Value && x.IsActive);
            if (mappingTemplate == null)
            {
                return BadRequest(new { message = "No mapping saved for this batch." });
            }

            var rowsQuery = tenantDb.ImportedRawRecords.AsNoTracking()
                .Where(x => x.ImportBatchId == id);

            if (!string.IsNullOrWhiteSpace(status))
            {
                var statusValue = status.Trim().ToUpperInvariant();
                rowsQuery = rowsQuery.Where(x => x.NormalizationStatus == statusValue);
            }

            var rows = await rowsQuery
                .OrderBy(x => x.RowNumber)
                .ToListAsync();

            if (rows.Count == 0)
            {
                return Ok(new ImportValidationRowsResponseDto(
                    id,
                    0,
                    0,
                    0,
                    new List<ImportValidationRowDto>()));
            }

            var allRows = await tenantDb.ImportedRawRecords.AsNoTracking()
                .Where(x => x.ImportBatchId == id)
                .ToListAsync();

            var validRows = allRows.Count(x => string.Equals(x.NormalizationStatus, "VALID", StringComparison.OrdinalIgnoreCase));
            var invalidRows = allRows.Count - validRows;

            var responseRows = rows.Select(x => new ImportValidationRowDto(
                x.ImportedRawRecordId,
                x.RowNumber ?? 0,
                x.NormalizationStatus,
                x.NormalizationErrors,
                DeserializeRowPayload(x.SourcePayloadJson)))
                .ToList();

            return Ok(new ImportValidationRowsResponseDto(
                id,
                allRows.Count,
                validRows,
                invalidRows,
                responseRows));
        }

        [HttpPut("{id:guid}/raw-records/{rawRecordId:guid}")]
        // WHY: Editing a raw row is a data-correction mutation — requires EDIT permission.
        [RequirePermission("ADMIN.IMPORTS.EDIT")]
        public async Task<ActionResult<ImportValidationRowDto>> UpdateRawRecord(
            Guid id,
            Guid rawRecordId,
            [FromBody] ImportUpdateRawRecordRequest request)
        {
            // Apply a correction to a single row and recompute validation.
            var auth = await AuthorizeTenantUserAsync();
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            var batch = await tenantDb.ImportBatches
                .FirstOrDefaultAsync(x => x.ImportBatchId == id);
            if (batch == null)
            {
                return NotFound();
            }

            if (!batch.MappingTemplateId.HasValue)
            {
                return BadRequest(new { message = "No mapping saved for this batch." });
            }

            var mappingTemplate = await tenantDb.ImportMappingTemplates.AsNoTracking()
                .FirstOrDefaultAsync(x => x.ImportMappingTemplateId == batch.MappingTemplateId.Value && x.IsActive);
            if (mappingTemplate == null)
            {
                return BadRequest(new { message = "No mapping saved for this batch." });
            }

            var rawRecord = await tenantDb.ImportedRawRecords
                .FirstOrDefaultAsync(x => x.ImportBatchId == id && x.ImportedRawRecordId == rawRecordId);
            if (rawRecord == null)
            {
                return NotFound();
            }

            if (request.Payload == null || request.Payload.Count == 0)
            {
                return BadRequest(new { message = "Payload is required." });
            }

            rawRecord.SourcePayloadJson = JsonSerializer.Serialize(request.Payload);

            var mappings = DeserializeMappings(mappingTemplate.MappingJson);
            var errors = _normalizationService.ValidateRow(request.Payload, mappings);

            if (errors.Count == 0)
            {
                rawRecord.NormalizationStatus = "VALID";
                rawRecord.NormalizationErrors = null;
            }
            else
            {
                rawRecord.NormalizationStatus = "INVALID";
                rawRecord.NormalizationErrors = string.Join(" | ", errors);
            }

            await UpdateBatchValidationStatusAsync(tenantDb, batch);
            await tenantDb.SaveChangesAsync();

            await _auditLogger.LogAsync(
                _userContext.UserId,
                "ImportRowCorrected",
                "ImportedRawRecord",
                rawRecordId.ToString(),
                $"batchId={id};status={rawRecord.NormalizationStatus}");

            return Ok(new ImportValidationRowDto(
                rawRecord.ImportedRawRecordId,
                rawRecord.RowNumber ?? 0,
                rawRecord.NormalizationStatus,
                rawRecord.NormalizationErrors,
                DeserializeRowPayload(rawRecord.SourcePayloadJson)));
        }

        [HttpGet("active-template")]
        // WHY: Reading the active template requires only VIEW — it's a read-only discovery call.
        [RequirePermission("ADMIN.IMPORTS.VIEW")]
        public async Task<ActionResult<ImportMappingTemplateSummaryDto>> GetActiveTemplate([FromQuery] string? sourceType)
        {
            // Fetch the most recent active template for the source type.
            var auth = await AuthorizeTenantUserAsync();
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            if (string.IsNullOrWhiteSpace(sourceType))
            {
                return BadRequest(new { message = "SourceType is required." });
            }

            var sourceTypeValue = sourceType.Trim();

            var template = await tenantDb.ImportMappingTemplates.AsNoTracking()
                .Where(x => x.IsActive && x.SourceType == sourceTypeValue)
                .OrderByDescending(x => x.UpdatedAt ?? x.CreatedAt)
                .FirstOrDefaultAsync();

            if (template is null)
            {
                return NotFound();
            }

            return Ok(new ImportMappingTemplateSummaryDto(
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

        [HttpPost("{id:guid}/parse")]
        // WHY: Parse + mapping + validate are EDIT-level operations (they mutate batch state).
        [RequirePermission("ADMIN.IMPORTS.EDIT")]
        public async Task<ActionResult<ImportParseResponseDto>> Parse(Guid id)
        {
            // Parse the stored file and create raw records.
            var auth = await AuthorizeTenantUserAsync();
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            var batch = await tenantDb.ImportBatches.FirstOrDefaultAsync(x => x.ImportBatchId == id);
            if (batch == null) return NotFound();

            // WHY: Verify source-type scope — a CASHIER may only parse their own POS batches.
            var parsePerms = await GetUserPermissionsAsync(tenantDb);
            if (!SourceTypeScope.IsAllowed(parsePerms, "IMPORTS", "EDIT", batch.SourceType))
                return Forbid();

            var filePath = ResolveStoredFilePath(auth.TenantId!.Value, id);
            if (filePath == null)
            {
                return NotFound(new { message = "Uploaded file not found for this batch." });
            }

            ParsedImportFile parsed;
            try
            {
                parsed = await _importFileParser.ParseAsync(filePath);
            }
            catch (Exception ex)
            {
                batch.Status = "PARSE_FAILED";
                batch.ErrorMessage = ex.Message;
                await tenantDb.SaveChangesAsync();
                return BadRequest(new { message = ex.Message });
            }

            var existingRaw = tenantDb.ImportedRawRecords.Where(x => x.ImportBatchId == id);
            tenantDb.ImportedRawRecords.RemoveRange(existingRaw);
            // Clear reconciliation artifacts tied to this batch before deleting normalized records.
            var existingEvents = tenantDb.ReconciliationEvents.Where(x => x.ImportBatchId == id);
            tenantDb.ReconciliationEvents.RemoveRange(existingEvents);
            var existingMatchGroups = tenantDb.ReconciliationMatchGroups.Where(x => x.ImportBatchId == id);
            tenantDb.ReconciliationMatchGroups.RemoveRange(existingMatchGroups);
            var existingNormalized = tenantDb.ImportedNormalizedRecords.Where(x => x.ImportBatchId == id);
            tenantDb.ImportedNormalizedRecords.RemoveRange(existingNormalized);

            // Persist parsed rows as raw records with pending validation status.
            var now = DateTime.UtcNow;
            for (var i = 0; i < parsed.Rows.Count; i++)
            {
                var payloadJson = JsonSerializer.Serialize(parsed.Rows[i]);
                tenantDb.ImportedRawRecords.Add(new ImportedRawRecord
                {
                    ImportedRawRecordId = Guid.NewGuid(),
                    ImportBatchId = id,
                    RowNumber = i + 1,
                    SourcePayloadJson = payloadJson,
                    NormalizationStatus = "PENDING",
                    CreatedAt = now
                });
            }

            batch.RawRecordCount = parsed.TotalRows;
            batch.NormalizedRecordCount = 0;
            batch.Status = "PARSED";
            batch.ErrorMessage = null;
            await tenantDb.SaveChangesAsync();

            var samples = parsed.Rows.Take(5).ToList();
            return Ok(new ImportParseResponseDto(
                id,
                batch.Status,
                parsed.Headers,
                samples,
                parsed.TotalRows));
        }

        [HttpPost("{id:guid}/mapping")]
        [RequirePermission("ADMIN.IMPORTS.EDIT")]
        public async Task<ActionResult<ImportMappingSavedResponseDto>> SaveMapping(Guid id, [FromBody] SaveImportMappingRequest request)
        {
            // Save field mappings and attach them to the batch.
            var auth = await AuthorizeTenantUserAsync();
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            var batch = await tenantDb.ImportBatches.FirstOrDefaultAsync(x => x.ImportBatchId == id);
            if (batch == null) return NotFound();

            // WHY: Source-type scope — mapping is part of the EDIT action chain.
            var mapPerms = await GetUserPermissionsAsync(tenantDb);
            if (!SourceTypeScope.IsAllowed(mapPerms, "IMPORTS", "EDIT", batch.SourceType))
                return Forbid();

            if (request.FieldMappings == null || request.FieldMappings.Count == 0)
            {
                return BadRequest(new { message = "FieldMappings is required." });
            }

            var mappingJson = JsonSerializer.Serialize(request.FieldMappings);
            var schemaVersion = string.IsNullOrWhiteSpace(request.CanonicalSchemaVersion)
                ? "v1"
                : request.CanonicalSchemaVersion.Trim();

            ImportMappingTemplate? existing = null;
            if (batch.MappingTemplateId.HasValue)
            {
                existing = await tenantDb.ImportMappingTemplates.FirstOrDefaultAsync(x => x.ImportMappingTemplateId == batch.MappingTemplateId.Value);
            }

            if (existing == null)
            {
                existing = new ImportMappingTemplate
                {
                    ImportMappingTemplateId = Guid.NewGuid(),
                    Name = $"Batch Mapping {id:N}",
                    SourceType = batch.SourceType,
                    CanonicalSchemaVersion = schemaVersion,
                    MappingJson = mappingJson,
                    Version = 1,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    CreatedByUserId = _userContext.UserId
                };
                tenantDb.ImportMappingTemplates.Add(existing);
                batch.MappingTemplateId = existing.ImportMappingTemplateId;
            }
            else
            {
                existing.SourceType = batch.SourceType;
                existing.CanonicalSchemaVersion = schemaVersion;
                existing.MappingJson = mappingJson;
                existing.IsActive = true;
                existing.Version += 1;
                existing.UpdatedAt = DateTime.UtcNow;
            }

            batch.Status = "MAPPED";
            batch.ErrorMessage = null;
            await tenantDb.SaveChangesAsync();

            await _auditLogger.LogAsync(
                _userContext.UserId,
                existing.Version == 1 ? "ImportMappingSaved" : "ImportMappingUpdated",
                "ImportBatch",
                id.ToString(),
                $"templateId={existing.ImportMappingTemplateId};version={existing.Version};sourceType={batch.SourceType}");

            return Ok(new ImportMappingSavedResponseDto(
                id,
                existing.Version,
                existing.CanonicalSchemaVersion,
                existing.UpdatedAt ?? existing.CreatedAt));
        }

        [HttpPost("{id:guid}/validate")]
        [RequirePermission("ADMIN.IMPORTS.EDIT")]
        public async Task<ActionResult<ImportValidateResponseDto>> Validate(Guid id)
        {
            // Validate all raw records for the batch.
            var auth = await AuthorizeTenantUserAsync();
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            var batch = await tenantDb.ImportBatches.FirstOrDefaultAsync(x => x.ImportBatchId == id);
            if (batch == null) return NotFound();

            // WHY: Source-type scope — validate is part of the EDIT action chain.
            var valPerms = await GetUserPermissionsAsync(tenantDb);
            if (!SourceTypeScope.IsAllowed(valPerms, "IMPORTS", "EDIT", batch.SourceType))
                return Forbid();

            if (!batch.MappingTemplateId.HasValue)
            {
                return BadRequest(new { message = "No mapping saved for this batch." });
            }

            var mappingTemplate = await tenantDb.ImportMappingTemplates.AsNoTracking()
                .FirstOrDefaultAsync(x => x.ImportMappingTemplateId == batch.MappingTemplateId.Value && x.IsActive);
            if (mappingTemplate == null)
            {
                return BadRequest(new { message = "No mapping saved for this batch." });
            }

            var mappings = DeserializeMappings(mappingTemplate.MappingJson);
            var rawRecords = await tenantDb.ImportedRawRecords
                .Where(x => x.ImportBatchId == id)
                .OrderBy(x => x.RowNumber)
                .ToListAsync();

            if (rawRecords.Count == 0)
            {
                return BadRequest(new { message = "No parsed records found. Parse the file first." });
            }

            var errors = new List<ImportValidationErrorDto>();
            var validRows = 0;
            foreach (var raw in rawRecords)
            {
                var row = DeserializeRowPayload(raw.SourcePayloadJson);
                var rowErrors = _normalizationService.ValidateRow(row, mappings);
                if (rowErrors.Count == 0)
                {
                    raw.NormalizationStatus = "VALID";
                    raw.NormalizationErrors = null;
                    validRows += 1;
                }
                else
                {
                    raw.NormalizationStatus = "INVALID";
                    raw.NormalizationErrors = string.Join(" | ", rowErrors);
                    foreach (var message in rowErrors)
                    {
                        errors.Add(new ImportValidationErrorDto(raw.RowNumber ?? 0, message));
                    }
                }
            }

            batch.Status = errors.Count == 0 ? "VALIDATED" : "VALIDATION_FAILED";
            batch.ErrorMessage = errors.Count == 0 ? null : $"{errors.Count} validation issue(s) found.";
            await tenantDb.SaveChangesAsync();

            return Ok(new ImportValidateResponseDto(
                id,
                batch.Status,
                rawRecords.Count,
                validRows,
                rawRecords.Count - validRows,
                errors.Take(200).ToList()));
        }

        [HttpPost("{id:guid}/commit")]
        // WHY: Commit is an irreversible, high-stakes action — its own permission lets ADMIN
        // grant upload+edit to MANAGER while retaining exclusive commit authority for ADMIN only.
        [RequirePermission("ADMIN.IMPORTS.COMMIT")]
        public async Task<ActionResult<ImportCommitResponseDto>> Commit(Guid id)
        {
            // Commit normalized records and trigger reconciliation.
            var auth = await AuthorizeTenantUserAsync();
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            var batch = await tenantDb.ImportBatches.FirstOrDefaultAsync(x => x.ImportBatchId == id);
            if (batch == null) return NotFound();

            // WHY: Source-type scope — CASHIER may commit POS batches if granted POS.COMMIT.
            var commitPerms = await GetUserPermissionsAsync(tenantDb);
            if (!SourceTypeScope.IsAllowed(commitPerms, "IMPORTS", "COMMIT", batch.SourceType))
                return Forbid();

            if (!batch.MappingTemplateId.HasValue)
            {
                return BadRequest(new { message = "No mapping saved for this batch." });
            }

            var mappingTemplate = await tenantDb.ImportMappingTemplates.AsNoTracking()
                .FirstOrDefaultAsync(x => x.ImportMappingTemplateId == batch.MappingTemplateId.Value && x.IsActive);
            if (mappingTemplate == null)
            {
                return BadRequest(new { message = "No mapping saved for this batch." });
            }

            var mappings = DeserializeMappings(mappingTemplate.MappingJson);
            var rawRecords = await tenantDb.ImportedRawRecords
                .Where(x => x.ImportBatchId == id)
                .OrderBy(x => x.RowNumber)
                .ToListAsync();

            if (rawRecords.Count == 0)
            {
                return BadRequest(new { message = "No parsed records found. Parse the file first." });
            }

            if (rawRecords.Any(x => !string.Equals(x.NormalizationStatus, "VALID", StringComparison.OrdinalIgnoreCase)))
            {
                return BadRequest(new { message = "All rows must be VALID before commit." });
            }

            await using var transaction = await tenantDb.Database.BeginTransactionAsync();

            var existingNormalized = tenantDb.ImportedNormalizedRecords.Where(x => x.ImportBatchId == id);
            tenantDb.ImportedNormalizedRecords.RemoveRange(existingNormalized);

            var normalizedRecords = new List<ImportedNormalizedRecord>(rawRecords.Count);
            foreach (var raw in rawRecords)
            {
                var row = DeserializeRowPayload(raw.SourcePayloadJson);
                // Normalize raw payload into canonical fields using the saved mappings.
                var result = _normalizationService.Normalize(id, raw.ImportedRawRecordId, row, mappings);
                if (result.Errors.Count > 0)
                {
                    return BadRequest(new { message = "Normalization errors detected. Re-run validation." });
                }
                normalizedRecords.Add(result.Normalized);
            }

            tenantDb.ImportedNormalizedRecords.AddRange(normalizedRecords);
            batch.NormalizedRecordCount = normalizedRecords.Count;
            batch.Status = "COMMITTED";
            batch.ErrorMessage = null;

            await tenantDb.SaveChangesAsync();

            var execution = await _reconciliationExecutionService.ExecuteOnCommitAsync(
                tenantDb,
                batch,
                normalizedRecords,
                HttpContext?.RequestAborted ?? CancellationToken.None);

            // Persist the latest workflow summary on the batch for quick operational visibility.
            batch.ErrorMessage = execution.Summary;
            await tenantDb.SaveChangesAsync();

            await transaction.CommitAsync();

            await _auditLogger.LogAsync(
                _userContext.UserId,
                "ImportCommit",
                "ImportBatch",
                id.ToString(),
                $"normalizedCount={normalizedRecords.Count};sourceType={batch.SourceType};workflowRoute={_reconciliationOrchestrator.DescribeRouting(batch.SourceType)};level3Verified={execution.Level3VerifiedCount};level3Exceptions={execution.Level3ExceptionCount};level4Matched={execution.Level4MatchedCount};level4Exceptions={execution.Level4ExceptionCount};waitingForSettlement={execution.WaitingForSettlementCount};feeAdjustmentTotal={execution.FeeAdjustmentTotal:0.##}");

            return Ok(new ImportCommitResponseDto(
                id,
                batch.Status,
                normalizedRecords.Count,
                DateTime.UtcNow,
                new ReconciliationSummaryDto(
                    execution.SourceType,
                    _reconciliationOrchestrator.DescribeRouting(batch.SourceType),
                    execution.Level3VerifiedCount,
                    execution.Level3ExceptionCount,
                    execution.Level4MatchedCount,
                    execution.Level4ExceptionCount,
                    execution.WaitingForSettlementCount,
                    execution.FeeAdjustmentTotal,
                    execution.Summary)));
        }

        [HttpDelete("{id:guid}")]
        // WHY: Delete is the most destructive action — exclusively for ADMIN.
        // MANAGE grants also satisfy this via the AliasMap implication.
        [RequirePermission("ADMIN.IMPORTS.DELETE")]
        public async Task<ActionResult<ImportDeleteResponseDto>> Delete(Guid id)
        {
            // Delete batch data and remove stored file if present.
            var auth = await AuthorizeTenantUserAsync();
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            var batch = await tenantDb.ImportBatches
                .FirstOrDefaultAsync(x => x.ImportBatchId == id);
            if (batch == null)
            {
                return NotFound();
            }

            await using var transaction = await tenantDb.Database.BeginTransactionAsync();

            var existingRaw = tenantDb.ImportedRawRecords.Where(x => x.ImportBatchId == id);
            tenantDb.ImportedRawRecords.RemoveRange(existingRaw);

            var existingNormalized = tenantDb.ImportedNormalizedRecords.Where(x => x.ImportBatchId == id);
            tenantDb.ImportedNormalizedRecords.RemoveRange(existingNormalized);

            tenantDb.ImportBatches.Remove(batch);
            await tenantDb.SaveChangesAsync();
            await transaction.CommitAsync();

            var fileDeleted = false;
            var filePath = ResolveStoredFilePath(auth.TenantId!.Value, id);
            if (!string.IsNullOrWhiteSpace(filePath) && System.IO.File.Exists(filePath))
            {
                try
                {
                    System.IO.File.Delete(filePath);
                    fileDeleted = true;
                }
                catch
                {
                    fileDeleted = false;
                }
            }

            return Ok(new ImportDeleteResponseDto(id, fileDeleted, DateTime.UtcNow));
        }

        /// <summary>
        /// WHY: The inner auth helper now only validates tenant membership and active status.
        /// Permission enforcement (CREATE/EDIT/COMMIT/DELETE/VIEW) is handled declaratively
        /// via [RequirePermission] attributes, which flow through PermissionHandler and support
        /// the full AliasMap implication chain (e.g. COMMIT implies VIEW).
        /// </summary>
        private async Task<(TenantDbContext? Db, Guid? TenantId, ActionResult? Error)> AuthorizeTenantUserAsync()
        {
            if (_userContext.UserId is not { } userId)
            {
                return (null, null, Unauthorized());
            }

            if (!_userContext.IsActive || _userContext.Status == UserStatus.Suspended || _userContext.Status == UserStatus.Banned)
            {
                return (null, null, Forbid());
            }

            var tenant = await _tenantContext.ResolveAsync();
            if (tenant == null || tenant.Status != TenantStatus.Active)
            {
                return (null, null, Forbid());
            }

            var tenantDb = await _tenantDbContextFactory.CreateAsync(tenant.TenantId);
            // Validate active tenant membership before allowing import operations.
            var isActiveInTenant = await tenantDb.TenantUsers.AsNoTracking().AnyAsync(tu => tu.UserId == userId && tu.IsActive);
            if (!isActiveInTenant)
            {
                await tenantDb.DisposeAsync();
                return (null, null, Forbid());
            }

            return (tenantDb, tenant.TenantId, null);
        }

        /// <summary>
        /// WHY: Loads the flat permission code list for the current user from the tenant DB.
        /// This is needed by SourceTypeScope to check source-type–scoped sub-permissions
        /// AFTER the coarse [RequirePermission] attribute has already passed.
        /// </summary>
        private async Task<IReadOnlyList<string>> GetUserPermissionsAsync(TenantDbContext tenantDb)
        {
            // Load permission codes for the current user from tenant roles.
            if (_userContext.UserId is not { } userId)
                return Array.Empty<string>();

            return await tenantDb.UserRoles
                .AsNoTracking()
                .Where(ur => ur.UserId == userId && ur.Role.IsActive)
                .SelectMany(ur => ur.Role.RolePermissions.Select(rp => rp.Permission.Code))
                .Distinct()
                .ToListAsync();
        }

        private static Dictionary<string, string> DeserializeMappings(string json)
        {
            // Normalize mappings to a case-insensitive lookup.
            var map = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            return new Dictionary<string, string>(map, StringComparer.OrdinalIgnoreCase);
        }

        private static Dictionary<string, string?> DeserializeRowPayload(string json)
        {
            // Deserialize a raw row payload into a case-insensitive dictionary.
            var row = JsonSerializer.Deserialize<Dictionary<string, string?>>(json)
                ?? new Dictionary<string, string?>();
            return new Dictionary<string, string?>(row, StringComparer.OrdinalIgnoreCase);
        }

        private static async Task UpdateBatchValidationStatusAsync(TenantDbContext tenantDb, ImportBatch batch)
        {
            // Update the batch status based on current validation results.
            var total = await tenantDb.ImportedRawRecords.CountAsync(x => x.ImportBatchId == batch.ImportBatchId);
            var valid = await tenantDb.ImportedRawRecords.CountAsync(x => x.ImportBatchId == batch.ImportBatchId && x.NormalizationStatus == "VALID");
            var invalid = total - valid;

            batch.Status = invalid == 0 && total > 0 ? "VALIDATED" : "VALIDATION_FAILED";
            batch.ErrorMessage = invalid == 0 ? null : $"{invalid} validation issue(s) found.";
        }


        private static string? ResolveStoredFilePath(Guid tenantId, Guid batchId)
        {
            // Locate the stored import file for the tenant and batch.
            var dir = Path.Combine(Directory.GetCurrentDirectory(), "App_Data", "imports", tenantId.ToString("N"));
            if (!Directory.Exists(dir))
            {
                return null;
            }

            foreach (var extension in SupportedExtensions)
            {
                var path = Path.Combine(dir, $"{batchId:N}{extension}");
                if (System.IO.File.Exists(path))
                {
                    return path;
                }
            }

            return null;
        }
    }
}
