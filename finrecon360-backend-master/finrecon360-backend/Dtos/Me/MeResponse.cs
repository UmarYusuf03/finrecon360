namespace finrecon360_backend.Dtos.Me
{
    public record MeResponse(
        Guid UserId,
        string Email,
        string DisplayName,
        string Status,
        Guid? TenantId,
        string? TenantName,
        string? TenantStatus,
        IReadOnlyList<string> Roles,
        IReadOnlyList<string> Permissions);
}
