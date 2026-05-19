using Microsoft.AspNetCore.Authorization;

namespace finrecon360_backend.Authorization
{
    /// <summary>
    /// WHY: Provides a declarative shorthand for permission-based authorization on endpoints.
    /// Instead of writing [Authorize(Policy = "ADMIN.IMPORTS.VIEW")], controllers can write [RequirePermission("ADMIN.IMPORTS.VIEW")].
    /// The attribute delegates all policy evaluation to PermissionPolicyProvider and PermissionHandler,
    /// keeping authorization logic centralized and testable.
    /// </summary>
    public sealed class RequirePermissionAttribute : AuthorizeAttribute
    {
        public RequirePermissionAttribute(string permissionCode)
        {
            Policy = $"{PermissionPolicyProvider.PolicyPrefix}{permissionCode}";
        }
    }
}
