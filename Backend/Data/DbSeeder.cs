using finrecon360_backend.Models;
using Microsoft.EntityFrameworkCore;

namespace finrecon360_backend.Data
{
    public static class DbSeeder
    {
        public static async Task SeedAsync(AppDbContext db)
        {
            var now = DateTime.UtcNow;

            var adminRole = await EnsureRoleAsync(db, "ADMIN", "Administrator", "System administrator", true, now);
            var userRole = await EnsureRoleAsync(db, "USER", "User", "Standard user", true, now);

            var permissions = new List<PermissionSeed>
            {
                new("ADMIN.USERS.MANAGE", "User Management", "Admin", "Manage users"),
                new("ADMIN.ROLES.MANAGE", "Role Management", "Admin", "Manage roles"),
                new("ADMIN.PERMISSIONS.MANAGE", "Permission Management", "Admin", "Manage permissions"),
                new("ADMIN.DASHBOARD.VIEW", "Admin Dashboard", "Admin", "Access admin dashboard"),
                new("ROLE_MANAGEMENT", "Role Management", "Admin", "Manage roles"),
                new("PERMISSION_MANAGEMENT", "Permission Management", "Admin", "Manage permissions"),
                new("USER_MANAGEMENT", "User Management", "Admin", "Manage users"),
                new("ADMIN_DASHBOARD", "Admin Dashboard", "Admin", "Access admin dashboard"),
                new("ADMIN.COMPONENTS.MANAGE", "Component Management", "Admin", "Manage admin components"),
                new("MATCHER.VIEW", "Matcher View", "Reconciliation", "View matcher"),
                new("MATCHER.MANAGE", "Matcher Manage", "Reconciliation", "Manage matcher"),
                new("BALANCER.VIEW", "Balancer View", "Reconciliation", "View balancer"),
                new("BALANCER.MANAGE", "Balancer Manage", "Reconciliation", "Manage balancer"),
                new("TASKS.VIEW", "Tasks View", "Reconciliation", "View tasks"),
                new("JOURNAL.VIEW", "Journal View", "Accounting", "View journal entries"),
                new("ANALYTICS.VIEW", "Analytics View", "Analytics", "View analytics"),
                new("BASIC_ACCESS", "Basic Access", "Core", "Baseline access")
            };

            var permissionEntities = new Dictionary<string, Permission>(StringComparer.OrdinalIgnoreCase);
            foreach (var permission in permissions)
            {
                var entity = await EnsurePermissionAsync(db, permission, now);
                permissionEntities[permission.Code] = entity;
            }

            foreach (var permission in permissionEntities.Values)
            {
                await EnsureRolePermissionAsync(db, adminRole.RoleId, permission.PermissionId, now);
            }

            var userBaselinePermissions = new[] { "BASIC_ACCESS" };
            foreach (var code in userBaselinePermissions)
            {
                if (permissionEntities.TryGetValue(code, out var permission))
                {
                    await EnsureRolePermissionAsync(db, userRole.RoleId, permission.PermissionId, now);
                }
            }

            await AssignAdminRoleToEmailsAsync(db, adminRole.RoleId, now);


            await SeedComponentsAsync(db, now);
            await SeedActionsAsync(db, now);

            await db.SaveChangesAsync();
        }

        private static async Task<Role> EnsureRoleAsync(AppDbContext db, string code, string name, string description, bool isSystem, DateTime now)
        {
            var role = await db.Roles.FirstOrDefaultAsync(r => r.Code == code)
                ?? await db.Roles.FirstOrDefaultAsync(r => r.Name == name);
            if (role != null)
            {
                var updated = false;
                if (string.IsNullOrWhiteSpace(role.Code))
                {
                    role.Code = code;
                    updated = true;
                }
                if (role.Name != name)
                {
                    role.Name = name;
                    updated = true;
                }
                if (role.Description != description)
                {
                    role.Description = description;
                    updated = true;
                }
                if (role.IsSystem != isSystem)
                {
                    role.IsSystem = isSystem;
                    updated = true;
                }
                if (updated)
                {
                    await db.SaveChangesAsync();
                }
                return role;
            }

            role = new Role
            {
                RoleId = Guid.NewGuid(),
                Code = code,
                Name = name,
                Description = description,
                IsSystem = isSystem,
                IsActive = true,
                CreatedAt = now
            };

            db.Roles.Add(role);
            await db.SaveChangesAsync();
            return role;
        }

        private static async Task<Permission> EnsurePermissionAsync(AppDbContext db, PermissionSeed permission, DateTime now)
        {
            var existing = await db.Permissions.FirstOrDefaultAsync(p => p.Code == permission.Code);
            if (existing != null)
            {
                return existing;
            }

            var entity = new Permission
            {
                PermissionId = Guid.NewGuid(),
                Code = permission.Code,
                Name = permission.Name,
                Module = permission.Module,
                Description = permission.Description,
                CreatedAt = now
            };

            db.Permissions.Add(entity);
            await db.SaveChangesAsync();
            return entity;
        }

        private static async Task EnsureRolePermissionAsync(AppDbContext db, Guid roleId, Guid permissionId, DateTime now)
        {
            var exists = await db.RolePermissions.AnyAsync(rp => rp.RoleId == roleId && rp.PermissionId == permissionId);
            if (exists)
            {
                return;
            }

            db.RolePermissions.Add(new RolePermission
            {
                RoleId = roleId,
                PermissionId = permissionId,
                GrantedAt = now
            });
        }

        private static async Task AssignAdminRoleToEmailsAsync(AppDbContext db, Guid adminRoleId, DateTime now)
        {
            var emailsValue = Environment.GetEnvironmentVariable("ADMIN_EMAILS");
            if (string.IsNullOrWhiteSpace(emailsValue))
            {
                return;
            }

            var emails = emailsValue
                .Split(new[] { ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (emails.Count == 0)
            {
                return;
            }

            var users = await db.Users
                .Where(u => emails.Contains(u.Email))
                .ToListAsync();

            foreach (var user in users)
            {
                var exists = await db.UserRoles.AnyAsync(ur => ur.UserId == user.UserId && ur.RoleId == adminRoleId);
                if (!exists)
                {
                    db.UserRoles.Add(new UserRole
                    {
                        UserId = user.UserId,
                        RoleId = adminRoleId,
                        AssignedAt = now
                    });
                }
            }
        }

        private record PermissionSeed(string Code, string Name, string Module, string Description);

        private static async Task SeedComponentsAsync(AppDbContext db, DateTime now)
        {
            var components = new List<(string Code, string Name, string RoutePath, string Category, string Description)>
            {
                ("DASHBOARD", "Dashboard", "/app/dashboard", "Analytics", "Landing overview"),
                ("MATCHER", "Matcher", "/app/matcher", "Reconciliation", "Matcher workspace"),
                ("BALANCER", "Balancer", "/app/balancer", "Reconciliation", "Balancer workspace"),
                ("TASK_MANAGER", "Task Manager", "/app/tasks", "Close Tasks", "Close tasks workflow"),
                ("JOURNAL_ENTRY", "Journal Entry", "/app/journal", "Accounting", "Journal entry"),
                ("ANALYTICS", "Analytics", "/app/analytics", "Analytics", "Analytics dashboards"),
                ("USER_MGMT", "User Management", "/app/admin/users", "Admin", "Admin users"),
                ("ROLE_MGMT", "Role Management", "/app/admin/roles", "Admin", "Admin roles"),
                ("PERMISSION_MGMT", "Permission Management", "/app/admin/permissions", "Admin", "Admin permissions")
            };

            foreach (var component in components)
            {
                var existing = await db.AppComponents.FirstOrDefaultAsync(c => c.Code == component.Code);
                if (existing != null)
                {
                    continue;
                }

                db.AppComponents.Add(new AppComponent
                {
                    AppComponentId = Guid.NewGuid(),
                    Code = component.Code,
                    Name = component.Name,
                    RoutePath = component.RoutePath,
                    Category = component.Category,
                    Description = component.Description,
                    IsActive = true,
                    CreatedAt = now
                });
            }
        }

        private static async Task SeedActionsAsync(AppDbContext db, DateTime now)
        {
            var actions = new List<(string Code, string Name, string Description)>
            {
                ("VIEW", "View", "Read access"),
                ("VIEW_LIST", "View List", "List access"),
                ("CREATE", "Create", "Create access"),
                ("EDIT", "Edit", "Edit access"),
                ("DELETE", "Delete", "Delete access"),
                ("APPROVE", "Approve", "Approve access"),
                ("MANAGE", "Manage", "Manage access")
            };

            foreach (var action in actions)
            {
                var existing = await db.PermissionActions.FirstOrDefaultAsync(a => a.Code == action.Code);
                if (existing != null)
                {
                    continue;
                }

                db.PermissionActions.Add(new PermissionAction
                {
                    PermissionActionId = Guid.NewGuid(),
                    Code = action.Code,
                    Name = action.Name,
                    Description = action.Description,
                    IsActive = true,
                    CreatedAt = now
                });
            }
        }
    }
}
