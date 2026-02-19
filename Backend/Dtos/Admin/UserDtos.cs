namespace finrecon360_backend.Dtos.Admin
{
    public record AdminUserSummaryDto(
        Guid Id,
        string Email,
        string DisplayName,
        bool IsActive,
        IReadOnlyList<string> Roles);

    public record AdminUserDetailDto(
        Guid Id,
        string Email,
        string DisplayName,
        bool IsActive,
        IReadOnlyList<RoleSummaryDto> Roles);

    public class AdminUserCreateRequest
    {
        public string Email { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string? PhoneNumber { get; set; }
        public IReadOnlyList<Guid>? RoleIds { get; set; }
        public IReadOnlyList<string>? RoleCodes { get; set; }
    }

    public class AdminUserUpdateRequest
    {
        public string DisplayName { get; set; } = string.Empty;
        public string? PhoneNumber { get; set; }
    }

    public class AdminUserRoleSetRequest
    {
        public IReadOnlyList<Guid>? RoleIds { get; set; }
        public IReadOnlyList<string>? RoleCodes { get; set; }
    }
}
