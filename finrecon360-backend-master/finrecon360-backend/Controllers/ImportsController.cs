using System.Globalization;
using System.Text.Json;
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

        private static readonly string[] RequiredCanonicalFields =
        {
            "TransactionDate",
            "DebitAmount",
            "CreditAmount",
            "NetAmount",
            "Currency"
        };

        private readonly AppDbContext _dbContext;
        private readonly ITenantContext _tenantContext;
        private readonly ITenantDbContextFactory _tenantDbContextFactory;
        private readonly IUserContext _userContext;
        private readonly IImportFileParser _importFileParser;

        public ImportsController(
            AppDbContext dbContext,
            ITenantContext tenantContext,
            ITenantDbContextFactory tenantDbContextFactory,
            IUserContext userContext,
            IImportFileParser importFileParser)
        {
            _dbContext = dbContext;
            _tenantContext = tenantContext;
            _tenantDbContextFactory = tenantDbContextFactory;
            _userContext = userContext;
            _importFileParser = importFileParser;
        }

        [HttpPost]
        [RequestSizeLimit(25 * 1024 * 1024)]
        public async Task<ActionResult<ImportUploadResponseDto>> Upload([FromForm] IFormFile file, [FromForm] string? sourceType = null)
        {
            var auth = await AuthorizeTenantUserAsync(requireAdmin: false);
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
                ? extension.TrimStart('.').ToUpperInvariant()
                : sourceType.Trim().ToUpperInvariant();

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
        public async Task<ActionResult<ImportHistoryResponseDto>> GetHistory(
            [FromQuery] string? search = null,
            [FromQuery] string? status = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            var auth = await AuthorizeTenantUserAsync(requireAdmin: false);
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 200);

            var query = tenantDb.ImportBatches.AsNoTracking();

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
            var items = await query
                .OrderByDescending(x => x.ImportedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new ImportHistoryItemDto(
                    x.ImportBatchId,
                    x.SourceType,
                    x.Status,
                    x.ImportedAt,
                    x.OriginalFileName,
                    x.RawRecordCount,
                    x.NormalizedRecordCount,
                    x.ErrorMessage))
                .ToListAsync();

            return Ok(new ImportHistoryResponseDto(items, total, page, pageSize));
        }

        [HttpPost("{id:guid}/parse")]
        public async Task<ActionResult<ImportParseResponseDto>> Parse(Guid id)
        {
            var auth = await AuthorizeTenantUserAsync(requireAdmin: true);
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            var batch = await tenantDb.ImportBatches.FirstOrDefaultAsync(x => x.ImportBatchId == id);
            if (batch == null)
            {
                return NotFound();
            }

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
            var existingNormalized = tenantDb.ImportedNormalizedRecords.Where(x => x.ImportBatchId == id);
            tenantDb.ImportedNormalizedRecords.RemoveRange(existingNormalized);

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
        public async Task<ActionResult<ImportMappingSavedResponseDto>> SaveMapping(Guid id, [FromBody] SaveImportMappingRequest request)
        {
            var auth = await AuthorizeTenantUserAsync(requireAdmin: true);
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            var batch = await tenantDb.ImportBatches.FirstOrDefaultAsync(x => x.ImportBatchId == id);
            if (batch == null)
            {
                return NotFound();
            }

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

            return Ok(new ImportMappingSavedResponseDto(
                id,
                existing.Version,
                existing.CanonicalSchemaVersion,
                existing.UpdatedAt ?? existing.CreatedAt));
        }

        [HttpPost("{id:guid}/validate")]
        public async Task<ActionResult<ImportValidateResponseDto>> Validate(Guid id)
        {
            var auth = await AuthorizeTenantUserAsync(requireAdmin: true);
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            var batch = await tenantDb.ImportBatches.FirstOrDefaultAsync(x => x.ImportBatchId == id);
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
                var rowErrors = ValidateRow(raw, mappings);
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
        public async Task<ActionResult<ImportCommitResponseDto>> Commit(Guid id)
        {
            var auth = await AuthorizeTenantUserAsync(requireAdmin: true);
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            var batch = await tenantDb.ImportBatches.FirstOrDefaultAsync(x => x.ImportBatchId == id);
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
                normalizedRecords.Add(MapToNormalizedRecord(id, raw.ImportedRawRecordId, row, mappings));
            }

            tenantDb.ImportedNormalizedRecords.AddRange(normalizedRecords);
            batch.NormalizedRecordCount = normalizedRecords.Count;
            batch.Status = "COMMITTED";
            batch.ErrorMessage = null;

            await tenantDb.SaveChangesAsync();
            await transaction.CommitAsync();

            return Ok(new ImportCommitResponseDto(
                id,
                batch.Status,
                normalizedRecords.Count,
                DateTime.UtcNow));
        }

        [HttpDelete("{id:guid}")]
        public async Task<ActionResult<ImportDeleteResponseDto>> Delete(Guid id)
        {
            var auth = await AuthorizeTenantUserAsync(requireAdmin: true);
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

            if (batch.MappingTemplateId.HasValue)
            {
                var mappingTemplate = await tenantDb.ImportMappingTemplates
                    .FirstOrDefaultAsync(x => x.ImportMappingTemplateId == batch.MappingTemplateId.Value);
                if (mappingTemplate != null)
                {
                    tenantDb.ImportMappingTemplates.Remove(mappingTemplate);
                }
            }

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

        private async Task<(TenantDbContext? Db, Guid? TenantId, ActionResult? Error)> AuthorizeTenantUserAsync(bool requireAdmin)
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

            var tenantMembership = await _dbContext.TenantUsers
                .AsNoTracking()
                .FirstOrDefaultAsync(tu => tu.TenantId == tenant.TenantId && tu.UserId == userId);

            if (tenantMembership == null)
            {
                return (null, null, Forbid());
            }

            if (requireAdmin && tenantMembership.Role != TenantUserRole.TenantAdmin)
            {
                return (null, null, Forbid());
            }

            var tenantDb = await _tenantDbContextFactory.CreateAsync(tenant.TenantId);
            var isActiveInTenant = await tenantDb.TenantUsers.AsNoTracking().AnyAsync(tu => tu.UserId == userId && tu.IsActive);
            if (!isActiveInTenant)
            {
                await tenantDb.DisposeAsync();
                return (null, null, Forbid());
            }

            return (tenantDb, tenant.TenantId, null);
        }

        private static Dictionary<string, string> DeserializeMappings(string json)
        {
            var map = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            return new Dictionary<string, string>(map, StringComparer.OrdinalIgnoreCase);
        }

        private static Dictionary<string, string?> DeserializeRowPayload(string json)
        {
            var row = JsonSerializer.Deserialize<Dictionary<string, string?>>(json)
                ?? new Dictionary<string, string?>();
            return new Dictionary<string, string?>(row, StringComparer.OrdinalIgnoreCase);
        }

        private static List<string> ValidateRow(ImportedRawRecord raw, Dictionary<string, string> mappings)
        {
            var errors = new List<string>();
            var row = DeserializeRowPayload(raw.SourcePayloadJson);

            foreach (var requiredField in RequiredCanonicalFields)
            {
                if (!mappings.TryGetValue(requiredField, out var sourceColumn) || string.IsNullOrWhiteSpace(sourceColumn))
                {
                    errors.Add($"Mapping missing for required field '{requiredField}'.");
                    continue;
                }

                row.TryGetValue(sourceColumn, out var value);
                if (string.IsNullOrWhiteSpace(value))
                {
                    errors.Add($"Required field '{requiredField}' is empty.");
                }
            }

            TryReadDate(row, mappings, "TransactionDate", errors);
            TryReadDecimal(row, mappings, "DebitAmount", errors);
            TryReadDecimal(row, mappings, "CreditAmount", errors);
            TryReadDecimal(row, mappings, "NetAmount", errors);

            return errors;
        }

        private static ImportedNormalizedRecord MapToNormalizedRecord(
            Guid batchId,
            Guid rawRecordId,
            Dictionary<string, string?> row,
            Dictionary<string, string> mappings)
        {
            var transactionDate = ReadDate(row, mappings, "TransactionDate");
            var postingDate = TryReadDate(row, mappings, "PostingDate");
            var referenceNumber = ReadString(row, mappings, "ReferenceNumber");
            var description = ReadString(row, mappings, "Description");
            var accountCode = ReadString(row, mappings, "AccountCode");
            var accountName = ReadString(row, mappings, "AccountName");
            var debit = ReadDecimal(row, mappings, "DebitAmount");
            var credit = ReadDecimal(row, mappings, "CreditAmount");
            var net = ReadDecimal(row, mappings, "NetAmount");
            var currency = (ReadString(row, mappings, "Currency") ?? "LKR").ToUpperInvariant();

            return new ImportedNormalizedRecord
            {
                ImportedNormalizedRecordId = Guid.NewGuid(),
                ImportBatchId = batchId,
                SourceRawRecordId = rawRecordId,
                TransactionDate = transactionDate,
                PostingDate = postingDate,
                ReferenceNumber = referenceNumber,
                Description = description,
                AccountCode = accountCode,
                AccountName = accountName,
                DebitAmount = debit,
                CreditAmount = credit,
                NetAmount = net,
                Currency = currency,
                CreatedAt = DateTime.UtcNow
            };
        }

        private static string? ResolveStoredFilePath(Guid tenantId, Guid batchId)
        {
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

        private static string? ReadString(Dictionary<string, string?> row, Dictionary<string, string> mappings, string canonicalField)
        {
            if (!mappings.TryGetValue(canonicalField, out var sourceColumn) || string.IsNullOrWhiteSpace(sourceColumn))
            {
                return null;
            }

            row.TryGetValue(sourceColumn, out var value);
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static decimal ReadDecimal(Dictionary<string, string?> row, Dictionary<string, string> mappings, string canonicalField)
        {
            var raw = ReadString(row, mappings, canonicalField) ?? throw new InvalidOperationException($"{canonicalField} is required.");
            if (decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var invariant))
            {
                return invariant;
            }
            if (decimal.TryParse(raw, NumberStyles.Any, CultureInfo.CurrentCulture, out var current))
            {
                return current;
            }

            throw new InvalidOperationException($"{canonicalField} is not a valid number.");
        }

        private static DateTime ReadDate(Dictionary<string, string?> row, Dictionary<string, string> mappings, string canonicalField)
        {
            var raw = ReadString(row, mappings, canonicalField) ?? throw new InvalidOperationException($"{canonicalField} is required.");
            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var invariant))
            {
                return invariant;
            }
            if (DateTime.TryParse(raw, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out var current))
            {
                return current;
            }

            throw new InvalidOperationException($"{canonicalField} is not a valid date.");
        }

        private static DateTime? TryReadDate(Dictionary<string, string?> row, Dictionary<string, string> mappings, string canonicalField)
        {
            var raw = ReadString(row, mappings, canonicalField);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var invariant))
            {
                return invariant;
            }
            if (DateTime.TryParse(raw, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out var current))
            {
                return current;
            }

            return null;
        }

        private static void TryReadDate(Dictionary<string, string?> row, Dictionary<string, string> mappings, string canonicalField, List<string> errors)
        {
            var raw = ReadString(row, mappings, canonicalField);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return;
            }

            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out _))
            {
                return;
            }

            if (DateTime.TryParse(raw, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out _))
            {
                return;
            }

            errors.Add($"Field '{canonicalField}' has invalid date value '{raw}'.");
        }

        private static void TryReadDecimal(Dictionary<string, string?> row, Dictionary<string, string> mappings, string canonicalField, List<string> errors)
        {
            var raw = ReadString(row, mappings, canonicalField);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return;
            }

            if (decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out _))
            {
                return;
            }

            if (decimal.TryParse(raw, NumberStyles.Any, CultureInfo.CurrentCulture, out _))
            {
                return;
            }

            errors.Add($"Field '{canonicalField}' has invalid numeric value '{raw}'.");
        }
    }
}
