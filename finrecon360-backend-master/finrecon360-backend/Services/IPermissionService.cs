namespace finrecon360_backend.Services
{
    public interface IPermissionService
    {
        Task<HashSet<string>> GetPermissionsForUserAsync(Guid userId);
        Task<IReadOnlyList<string>> GetRolesForUserAsync(Guid userId);
    }
}
