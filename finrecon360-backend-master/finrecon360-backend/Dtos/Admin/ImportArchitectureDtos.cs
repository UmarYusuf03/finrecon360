using System.Text.Json;

namespace finrecon360_backend.Dtos.Admin
{
    public record CanonicalFieldDto(
        string Field,
        string DataType,
        bool Required,
        string Description);

    public record CanonicalSchemaDto(
        string Version,
        IReadOnlyList<CanonicalFieldDto> Fields);

    public record ImportArchitectureOverviewDto(
        int TotalImportBatches,
        int TotalRawRecords,
        int TotalNormalizedRecords,
        int ActiveMappingTemplates,
        DateTime? LatestImportAt,
        CanonicalSchemaDto CanonicalSchema);

    public record ImportBatchDto(
        Guid Id,
        string SourceType,
        string Status,
        DateTime ImportedAt,
        Guid? UploadedByUserId,
        string? OriginalFileName,
        int RawRecordCount,
        int NormalizedRecordCount,
        string? ErrorMessage);

    public class CreateImportBatchRequest
    {
        public string SourceType { get; set; } = string.Empty;
        public string Status { get; set; } = "RECEIVED";
        public string? OriginalFileName { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class ImportRawRecordRequest
    {
        public int? RowNumber { get; set; }
        public JsonElement SourcePayload { get; set; }
        public string NormalizationStatus { get; set; } = "PENDING";
        public string? NormalizationErrors { get; set; }
    }

    public class ImportNormalizedRecordRequest
    {
        public Guid? SourceRawRecordId { get; set; }
        public DateTime TransactionDate { get; set; }
        public DateTime? PostingDate { get; set; }
        public string? ReferenceNumber { get; set; }
        public string? Description { get; set; }
        public string? AccountCode { get; set; }
        public string? AccountName { get; set; }
        public decimal DebitAmount { get; set; }
        public decimal CreditAmount { get; set; }
        public decimal NetAmount { get; set; }
        public string Currency { get; set; } = "LKR";
    }

    public record ImportMappingTemplateDto(
        Guid Id,
        string Name,
        string SourceType,
        string CanonicalSchemaVersion,
        int Version,
        bool IsActive,
        string MappingJson,
        DateTime CreatedAt,
        DateTime? UpdatedAt);

    public class ImportMappingTemplateCreateRequest
    {
        public string Name { get; set; } = string.Empty;
        public string SourceType { get; set; } = string.Empty;
        public string CanonicalSchemaVersion { get; set; } = "v1";
        public string MappingJson { get; set; } = string.Empty;
    }

    public class ImportMappingTemplateUpdateRequest
    {
        public string Name { get; set; } = string.Empty;
        public string SourceType { get; set; } = string.Empty;
        public string CanonicalSchemaVersion { get; set; } = "v1";
        public string MappingJson { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
    }
}
