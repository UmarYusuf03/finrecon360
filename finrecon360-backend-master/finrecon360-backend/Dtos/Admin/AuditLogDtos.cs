namespace finrecon360_backend.Dtos.Admin
{
    public record AuditLogSummaryDto(
        Guid AuditLogId,
        Guid? UserId,
        string Action,
        string? Entity,
        string? EntityId,
        string? Metadata,
        DateTime CreatedAt,
        string? UserEmail,
        string? UserDisplayName);
}
