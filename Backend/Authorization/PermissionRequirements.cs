using Microsoft.AspNetCore.Authorization;

namespace finrecon360_backend.Authorization
{
    public class PermissionRequirement : IAuthorizationRequirement
    {
        public PermissionRequirement(string permissionCode)
        {
            PermissionCode = permissionCode;
        }

        public string PermissionCode { get; }
    }
}
