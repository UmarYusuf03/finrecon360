using System.Text.RegularExpressions;
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

        /// <summary>
        /// WHY: The AliasMap implements the "MANAGE→VIEW implication" rule.
        /// Any mutating permission (CREATE/EDIT/DELETE/COMMIT/CONFIRM/RESOLVE/POST/MANAGE) also satisfies
        /// a VIEW check for the same module. This is enforced here, centrally, so every endpoint
        /// that checks VIEW will also pass for users who only have a mutating grant — no per-controller
        /// logic needed, and no duplicate VIEW grants needed in the role seed.
        /// </summary>
        private static readonly Dictionary<string, string[]> AliasMap = new(StringComparer.OrdinalIgnoreCase)
        {
            // Legacy aliases (kept for backwards compatibility)
            { "ROLE_MANAGEMENT",       new[] { "ADMIN.ROLES.MANAGE" } },
            { "PERMISSION_MANAGEMENT", new[] { "ADMIN.PERMISSIONS.MANAGE" } },
            { "USER_MANAGEMENT",       new[] { "ADMIN.USERS.MANAGE" } },
            { "ADMIN_DASHBOARD",       new[] { "ADMIN.DASHBOARD.VIEW" } },

            // ── VIEW satisfied by any mutating permission on the same module ──────────
            // IMPORTS module
            { "ADMIN.IMPORTS.VIEW", new[]
            {
                "ADMIN.IMPORTS.CREATE",
                "ADMIN.IMPORTS.EDIT",
                "ADMIN.IMPORTS.COMMIT",
                "ADMIN.IMPORTS.DELETE",
                "ADMIN.IMPORTS.MANAGE",
                // Source-type scoped grants also imply VIEW of the import list
                // (the API will filter the visible batches to the allowed source types)
                "ADMIN.IMPORTS.POS.CREATE",
                "ADMIN.IMPORTS.POS.EDIT",
                "ADMIN.IMPORTS.POS.COMMIT",
                "ADMIN.IMPORTS.ERP.CREATE",
                "ADMIN.IMPORTS.ERP.EDIT",
                "ADMIN.IMPORTS.ERP.COMMIT",
                "ADMIN.IMPORTS.GATEWAY.CREATE",
                "ADMIN.IMPORTS.GATEWAY.EDIT",
                "ADMIN.IMPORTS.GATEWAY.COMMIT",
                "ADMIN.IMPORTS.BANK.CREATE",
                "ADMIN.IMPORTS.BANK.EDIT",
                "ADMIN.IMPORTS.BANK.COMMIT",
                // Legacy workbench code (kept for existing DB grants)
                "ADMIN.IMPORT_WORKBENCH.VIEW",
                "ADMIN.IMPORT_WORKBENCH.MANAGE",
            }},

            // POS-scoped IMPORTS: full IMPORTS.CREATE/EDIT/COMMIT/MANAGE also satisfy these
            // WHY: An ADMIN who has ADMIN.IMPORTS.CREATE must never be blocked by an endpoint
            // that checks the scoped ADMIN.IMPORTS.POS.CREATE code — higher wins.
            { "ADMIN.IMPORTS.POS.CREATE", new[] { "ADMIN.IMPORTS.CREATE", "ADMIN.IMPORTS.MANAGE" } },
            { "ADMIN.IMPORTS.POS.EDIT",   new[] { "ADMIN.IMPORTS.EDIT",   "ADMIN.IMPORTS.MANAGE" } },
            { "ADMIN.IMPORTS.POS.COMMIT", new[] { "ADMIN.IMPORTS.COMMIT", "ADMIN.IMPORTS.MANAGE" } },

            // IMPORT_ARCHITECTURE module
            { "ADMIN.IMPORT_ARCHITECTURE.VIEW", new[]
            {
                "ADMIN.IMPORT_ARCHITECTURE.CREATE",
                "ADMIN.IMPORT_ARCHITECTURE.EDIT",
                "ADMIN.IMPORT_ARCHITECTURE.DELETE",
                "ADMIN.IMPORT_ARCHITECTURE.MANAGE",
            }},

            // RECONCILIATION module
            { "ADMIN.RECONCILIATION.VIEW", new[]
            {
                "ADMIN.RECONCILIATION.CONFIRM",
                "ADMIN.RECONCILIATION.RESOLVE",
                "ADMIN.RECONCILIATION.MANAGE",
                // POS-scoped reconciliation grants also satisfy general VIEW
                "ADMIN.RECONCILIATION.POS.RESOLVE",
                "ADMIN.RECONCILIATION.ERP.RESOLVE",
                "ADMIN.RECONCILIATION.GATEWAY.RESOLVE",
                "ADMIN.RECONCILIATION.BANK.RESOLVE",
            }},

            // POS-scoped RECONCILIATION: full grants satisfy scoped equivalents
            { "ADMIN.RECONCILIATION.POS.RESOLVE", new[] { "ADMIN.RECONCILIATION.RESOLVE", "ADMIN.RECONCILIATION.MANAGE" } },
            { "ADMIN.RECONCILIATION.ERP.RESOLVE", new[] { "ADMIN.RECONCILIATION.RESOLVE", "ADMIN.RECONCILIATION.MANAGE" } },
            { "ADMIN.RECONCILIATION.GATEWAY.RESOLVE", new[] { "ADMIN.RECONCILIATION.RESOLVE", "ADMIN.RECONCILIATION.MANAGE" } },
            { "ADMIN.RECONCILIATION.BANK.RESOLVE", new[] { "ADMIN.RECONCILIATION.RESOLVE", "ADMIN.RECONCILIATION.MANAGE" } },

            // JOURNAL module
            { "ADMIN.JOURNAL.VIEW", new[]
            {
                "ADMIN.JOURNAL.POST",
                "ADMIN.JOURNAL.MANAGE",
            }},

            // TRANSACTIONS module
            { "ADMIN.TRANSACTIONS.VIEW", new[]
            {
                "ADMIN.TRANSACTIONS.CREATE",
                "ADMIN.TRANSACTIONS.EDIT",
                "ADMIN.TRANSACTIONS.APPROVE",
                "ADMIN.TRANSACTIONS.MANAGE",
            }},

            // BANK_ACCOUNTS module
            { "ADMIN.BANK_ACCOUNTS.VIEW", new[]
            {
                "ADMIN.BANK_ACCOUNTS.MANAGE",
            }},
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

        public static IReadOnlyList<string> ExpandPermissions(IEnumerable<string>? permissions)
        {
            if (permissions == null)
            {
                return Array.Empty<string>();
            }

            var expanded = new HashSet<string>(permissions.Where(permission => !string.IsNullOrWhiteSpace(permission)), StringComparer.OrdinalIgnoreCase);
            var hasScopedImportPermission = expanded.Any(permission =>
                Regex.IsMatch(permission, @"^ADMIN\.IMPORTS(\.[A-Z]+)?\.(CREATE|EDIT|COMMIT|DELETE|MANAGE)$", RegexOptions.IgnoreCase));

            if (hasScopedImportPermission)
            {
                expanded.Add("ADMIN.IMPORT_WORKBENCH.VIEW");
            }

            return expanded.ToList();
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

                permissions = ExpandPermissions(await tenantDb.UserRoles
                    .AsNoTracking()
                    .Where(ur => ur.UserId == userId.Value && ur.Role.IsActive)
                    .SelectMany(ur => ur.Role.RolePermissions.Select(rp => rp.Permission.Code))
                    .Distinct()
                    .ToListAsync());

                if (permissions.Contains(requirement.PermissionCode, StringComparer.OrdinalIgnoreCase))
                {
                    context.Succeed(requirement);
                    return;
                }

                var isCanonicalTenantAdmin = await tenantDb.TenantUsers
                    .AsNoTracking()
                    .AnyAsync(tu => tu.UserId == userId.Value && tu.IsActive && tu.Role == Models.TenantUserRole.TenantAdmin.ToString());

                if (isCanonicalTenantAdmin)
                {
                    context.Succeed(requirement);
                }
                }
            }
            else
            {
                if (!isControlPlanePermission)
                {
                    // Tenant permissions require a resolved tenant context.
                    return;
                }
                permissions = ExpandPermissions(await _permissionService.GetPermissionsForUserAsync(userId.Value));
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
