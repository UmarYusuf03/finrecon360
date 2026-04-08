namespace finrecon360_backend.Dtos.Admin
{
    public record RoleSummaryDto(
        Guid Id,
        string Code,
        string Name,
        string? Description,
        bool IsSystem,
        bool IsActive);

    public record RoleDetailDto(
        Guid Id,
        string Code,
        string Name,
        string? Description,
        bool IsSystem,
        bool IsActive,
        IReadOnlyList<PermissionSummaryDto> Permissions);

    public class RoleCreateRequest
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
    }

    public class RoleUpdateRequest
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
    }
}
