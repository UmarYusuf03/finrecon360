namespace finrecon360_backend.Dtos.Admin
{
    public record PermissionSummaryDto(
        Guid Id,
        string Code,
        string Name,
        string? Description,
        string? Module);

    public class PermissionCreateRequest
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? Module { get; set; }
    }

    public class PermissionUpdateRequest
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? Module { get; set; }
    }
}
