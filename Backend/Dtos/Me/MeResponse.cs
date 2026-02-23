namespace finrecon360_backend.Dtos.Me
{
    public record MeResponse(
        Guid UserId,
        string Email,
        string DisplayName,
        IReadOnlyList<string> Roles,
        IReadOnlyList<string> Permissions);
}
