namespace finrecon360_backend.Dtos.Admin
{
    public class RolePermissionSetRequest
    {
        public IReadOnlyList<Guid>? PermissionIds { get; set; }
        public IReadOnlyList<string>? PermissionCodes { get; set; }
    }
}
