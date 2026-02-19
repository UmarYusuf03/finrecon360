using Microsoft.AspNetCore.Authorization;

namespace finrecon360_backend.Authorization
{
    public sealed class RequirePermissionAttribute : AuthorizeAttribute
    {
        public RequirePermissionAttribute(string permissionCode)
        {
            Policy = $"{PermissionPolicyProvider.PolicyPrefix}{permissionCode}";
        }
    }
}
