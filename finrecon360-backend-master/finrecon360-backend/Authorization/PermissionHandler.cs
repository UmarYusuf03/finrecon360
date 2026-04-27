using finrecon360_backend.Data;
using finrecon360_backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace finrecon360_backend.Authorization
{
    /// <summary>
    /// WHY: This custom handler serves as the policy evaluation engine. It determines 
    /// if an endpoint required permission is a "Control Plane" (system-level) permission 
    /// or a Tenant-level permission, dynamically checking the correct database and user states.
    /// </summary>
    public class PermissionHandler : AuthorizationHandler<PermissionRequirement>
    {
        private static readonly HashSet<string> ControlPlanePermissions = new(StringComparer.OrdinalIgnoreCase)
        {
            "ADMIN.TENANT_REGISTRATIONS.MANAGE",
            "ADMIN.TENANTS.MANAGE",
            "ADMIN.PLANS.MANAGE",
            "ADMIN.ENFORCEMENT.MANAGE"
        };

        private static readonly Dictionary<string, string[]> AliasMap = new(StringComparer.OrdinalIgnoreCase)
        {
            { "ROLE_MANAGEMENT", new[] { "ADMIN.ROLES.MANAGE" } },
            { "PERMISSION_MANAGEMENT", new[] { "ADMIN.PERMISSIONS.MANAGE" } },
            { "USER_MANAGEMENT", new[] { "ADMIN.USERS.MANAGE" } },
            { "ADMIN_DASHBOARD", new[] { "ADMIN.DASHBOARD.VIEW" } }
        };

        private readonly IUserContext _userContext;
        private readonly IPermissionService _permissionService;
        private readonly ITenantContext _tenantContext;
        private readonly ITenantDbContextFactory _tenantDbContextFactory;
        private readonly AppDbContext _dbContext;

        public PermissionHandler(
            IUserContext userContext,
            IPermissionService permissionService,
            ITenantContext tenantContext,
            ITenantDbContextFactory tenantDbContextFactory,
            AppDbContext dbContext)
        {
            _userContext = userContext;
            _permissionService = permissionService;
            _tenantContext = tenantContext;
            _tenantDbContextFactory = tenantDbContextFactory;
            _dbContext = dbContext;
        }

        protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
        {
            var userId = _userContext.UserId;
            if (userId == null)
            {
                return;
            }

            if (!_userContext.IsActive || _userContext.Status == Models.UserStatus.Suspended || _userContext.Status == Models.UserStatus.Banned)
            {
                return;
            }

            var isControlPlanePermission = IsControlPlanePermission(requirement.PermissionCode);

            // System admins must not access tenant-scoped features (e.g. dashboard/matcher).
            // We enforce this centrally so accidental role/permission leakage can't grant tenant access.
            var isSystemAdmin = await _dbContext.Users
                .AsNoTracking()
                .Where(u => u.UserId == userId.Value)
                .Select(u => u.IsSystemAdmin)
                .FirstOrDefaultAsync();

            if (isSystemAdmin && !isControlPlanePermission)
            {
                return;
            }

            var tenant = await _tenantContext.ResolveAsync();
            IReadOnlyCollection<string> permissions;

            if (!isControlPlanePermission && tenant != null)
            {
                if (tenant.Status != Models.TenantStatus.Active)
                {
                    return;
                }

                TenantDbContext tenantDb;
                try
                {
                    tenantDb = await _tenantDbContextFactory.CreateAsync(tenant.TenantId);
                }
                catch (InvalidOperationException)
                {
                    // If tenant database is unavailable, deny permission instead of failing the request pipeline.
                    return;
                }

                await using (tenantDb)
                {
                // WHY: We explicitly instantiate the tenant DB and check `IsActive` constraints 
                // in real-time, because tenant administrators could have deactivated a user 
                // mere seconds ago, and we cannot rely on a stale JWT claim or static snapshot.
                var isActiveInTenant = await tenantDb.TenantUsers
                    .AsNoTracking()
                    .AnyAsync(tu => tu.UserId == userId.Value && tu.IsActive);
                if (!isActiveInTenant)
                {
                    return;
                }

                permissions = await tenantDb.UserRoles
                    .AsNoTracking()
                    .Where(ur => ur.UserId == userId.Value && ur.Role.IsActive)
                    .SelectMany(ur => ur.Role.RolePermissions.Select(rp => rp.Permission.Code))
                    .Distinct()
                    .ToListAsync();
                }
            }
            else
            {
                if (!isControlPlanePermission)
                {
                    // Tenant permissions require a resolved tenant context.
                    return;
                }
                permissions = await _permissionService.GetPermissionsForUserAsync(userId.Value);
            }

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

        private static bool IsControlPlanePermission(string permissionCode)
        {
            if (ControlPlanePermissions.Contains(permissionCode))
            {
                return true;
            }

            if (AliasMap.TryGetValue(permissionCode, out var aliases))
            {
                return aliases.Any(alias => ControlPlanePermissions.Contains(alias));
            }

            return false;
        }
    }
}
