namespace finrecon360_backend.Dtos.Imports
{
    public class CreateImportUploadRequest
    {
        public string? SourceType { get; set; }
    }

    public record ImportHistoryItemDto(
        Guid Id,
        string SourceType,
        string Status,
        DateTime ImportedAt,
        string? OriginalFileName,
        int RawRecordCount,
        int NormalizedRecordCount,
        string? ErrorMessage,
        Guid? UploadedByUserId,
        string? UploadedByEmail,
        string? UploadedByName);

    public record ImportHistoryResponseDto(
        IReadOnlyList<ImportHistoryItemDto> Items,
        int Total,
        int Page,
        int PageSize);

    public record ImportUploadResponseDto(
        Guid Id,
        string Status,
        string SourceType,
        string OriginalFileName,
        DateTime ImportedAt);

    public class SaveImportMappingRequest
    {
        public string? CanonicalSchemaVersion { get; set; }
        public Dictionary<string, string> FieldMappings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public record ImportMappingSavedResponseDto(
        Guid BatchId,
        int Version,
        string CanonicalSchemaVersion,
        DateTime SavedAt);

    public record ImportMappingTemplateSummaryDto(
        Guid Id,
        string Name,
        string SourceType,
        string CanonicalSchemaVersion,
        int Version,
        bool IsActive,
        string MappingJson,
        DateTime CreatedAt,
        DateTime? UpdatedAt);

    public record ImportParseResponseDto(
        Guid BatchId,
        string Status,
        IReadOnlyList<string> Headers,
        IReadOnlyList<Dictionary<string, string?>> SampleRows,
        int ParsedRowCount);

    public record ImportValidationErrorDto(
        int RowNumber,
        string Message);

    public record ImportValidateResponseDto(
        Guid BatchId,
        string Status,
        int TotalRows,
        int ValidRows,
        int InvalidRows,
        IReadOnlyList<ImportValidationErrorDto> Errors);

    public record ImportValidationRowDto(
        Guid RawRecordId,
        int RowNumber,
        string NormalizationStatus,
        string? NormalizationErrors,
        Dictionary<string, string?> Payload);

    public record ImportValidationRowsResponseDto(
        Guid BatchId,
        int TotalRows,
        int ValidRows,
        int InvalidRows,
        IReadOnlyList<ImportValidationRowDto> Rows);

    public class ImportUpdateRawRecordRequest
    {
        public Dictionary<string, string?> Payload { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public record ReconciliationSummaryDto(
        string SourceType,
        string WorkflowRoute,
        int Level3VerifiedCount,
        int Level3ExceptionCount,
        int Level4MatchedCount,
        int Level4ExceptionCount,
        int WaitingForSettlementCount,
        decimal FeeAdjustmentTotal,
        string Summary);

    public record ImportCommitResponseDto(
        Guid BatchId,
        string Status,
        int NormalizedCount,
        DateTime CommittedAt,
        ReconciliationSummaryDto ReconciliationSummary);

    public record ImportDeleteResponseDto(
        Guid BatchId,
        bool FileDeleted,
        DateTime DeletedAt);
}
