using finrecon360_backend.Services.Export;

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

    /// <summary>Shared column mapping so the tenant and system-admin audit log exports produce
    /// identical file shapes.</summary>
    public static class AuditLogExportColumns
    {
        public static readonly IReadOnlyList<ExportColumn<AuditLogSummaryDto>> Columns = new List<ExportColumn<AuditLogSummaryDto>>
        {
            new("Time (UTC)", a => a.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")),
            new("Action", a => a.Action),
            new("Entity", a => a.Entity),
            new("Entity ID", a => a.EntityId),
            new("User Email", a => a.UserEmail),
            new("User Display Name", a => a.UserDisplayName),
            new("User ID", a => a.UserId?.ToString()),
            new("Metadata", a => a.Metadata),
        };
    }
}
