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
        string? ErrorMessage);

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

    public record ImportCommitResponseDto(
        Guid BatchId,
        string Status,
        int NormalizedCount,
        DateTime CommittedAt);

    public record ImportDeleteResponseDto(
        Guid BatchId,
        bool FileDeleted,
        DateTime DeletedAt);
}
