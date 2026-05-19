using Microsoft.AspNetCore.Authorization;

namespace finrecon360_backend.Authorization
{
    /// <summary>
    /// WHY: Represents a single permission check requirement.
    /// When an endpoint is decorated with [RequirePermission("CODE")], the policy engine creates this requirement
    /// and passes it to PermissionHandler for evaluation. Isolating the requirement as a separate class keeps
    /// the authorization system composable and allows stacking multiple requirements if needed.
    /// </summary>
    public class PermissionRequirement : IAuthorizationRequirement
    {
        public PermissionRequirement(string permissionCode)
        {
            PermissionCode = permissionCode;
        }

        public string PermissionCode { get; }
    }
}
