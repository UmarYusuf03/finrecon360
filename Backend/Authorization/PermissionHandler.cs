using finrecon360_backend.Services;
using Microsoft.AspNetCore.Authorization;

namespace finrecon360_backend.Authorization
{
    public class PermissionHandler : AuthorizationHandler<PermissionRequirement>
    {
        private static readonly Dictionary<string, string[]> AliasMap = new(StringComparer.OrdinalIgnoreCase)
        {
            { "ROLE_MANAGEMENT", new[] { "ADMIN.ROLES.MANAGE" } },
            { "PERMISSION_MANAGEMENT", new[] { "ADMIN.PERMISSIONS.MANAGE" } },
            { "USER_MANAGEMENT", new[] { "ADMIN.USERS.MANAGE" } },
            { "ADMIN_DASHBOARD", new[] { "ADMIN.DASHBOARD.VIEW" } }
        };

        private readonly IUserContext _userContext;
        private readonly IPermissionService _permissionService;

        public PermissionHandler(IUserContext userContext, IPermissionService permissionService)
        {
            _userContext = userContext;
            _permissionService = permissionService;
        }

        protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
        {
            var userId = _userContext.UserId;
            if (userId == null)
            {
                return;
            }

            if (!_userContext.IsActive)
            {
                return;
            }

            var permissions = await _permissionService.GetPermissionsForUserAsync(userId.Value);
            if (permissions.Contains(requirement.PermissionCode, StringComparer.OrdinalIgnoreCase))
            {
                context.Succeed(requirement);
                return;
            }

            if (AliasMap.TryGetValue(requirement.PermissionCode, out var aliases) && aliases.Any(alias => permissions.Contains(alias, StringComparer.OrdinalIgnoreCase)))
            {
                context.Succeed(requirement);
            }
        }
    }
}
