using finrecon360_backend.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;

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
                new("ADMIN.TENANTS.MANAGE", "Tenant Management", "Admin", "Manage tenants"),
                new("ADMIN.TENANT_REGISTRATIONS.MANAGE", "Tenant Registrations", "Admin", "Review tenant registrations"),
                new("ADMIN.PLANS.MANAGE", "Subscription Plans", "Admin", "Manage subscription plans"),
                new("ADMIN.ENFORCEMENT.MANAGE", "Enforcement Actions", "Admin", "Manage suspensions and bans"),
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

            await SeedSystemAdminAsync(db, adminRole.RoleId, now);
            await SeedTemporaryTenantBypassAsync(db, userRole.RoleId, now);


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

        private static async Task SeedSystemAdminAsync(AppDbContext db, Guid adminRoleId, DateTime now)
        {
            var email = Environment.GetEnvironmentVariable("SYSTEM_ADMIN_EMAIL")?.Trim().ToLowerInvariant();
            var password = Environment.GetEnvironmentVariable("SYSTEM_ADMIN_PASSWORD");

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                return;
            }

            var firstName = Environment.GetEnvironmentVariable("SYSTEM_ADMIN_FIRST_NAME")?.Trim();
            var lastName = Environment.GetEnvironmentVariable("SYSTEM_ADMIN_LAST_NAME")?.Trim();
            var hasher = new Services.Sha256PasswordHasher();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);

            if (user == null)
            {
                user = new User
                {
                    UserId = Guid.NewGuid(),
                    Email = email,
                    DisplayName = email,
                    FirstName = string.IsNullOrWhiteSpace(firstName) ? "System" : firstName,
                    LastName = string.IsNullOrWhiteSpace(lastName) ? "Admin" : lastName,
                    Country = string.Empty,
                    Gender = string.Empty,
                    PasswordHash = hasher.Hash(password),
                    CreatedAt = now,
                    UpdatedAt = now,
                    EmailConfirmed = true,
                    IsActive = true,
                    Status = UserStatus.Active,
                    IsSystemAdmin = true
                };
                db.Users.Add(user);
            }
            else
            {
                user.PasswordHash = hasher.Hash(password);
                user.IsSystemAdmin = true;
                user.IsActive = true;
                user.Status = UserStatus.Active;
                user.EmailConfirmed = true;
                user.UpdatedAt = now;
            }

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

        private static async Task SeedTemporaryTenantBypassAsync(AppDbContext db, Guid userRoleId, DateTime now)
        {
            var enabledValue = Environment.GetEnvironmentVariable("TEMP_BYPASS_SEED_TENANT_ADMIN");
            if (!bool.TryParse(enabledValue, out var enabled) || !enabled)
            {
                return;
            }

            if (!await TableExistsAsync(db, "Tenants"))
            {
                Console.WriteLine(
                    "TEMP_BYPASS_SEED_TENANT_ADMIN is enabled, but control-plane tables are missing. " +
                    "Run migrations first; skipping temporary tenant bypass seeding.");
                return;
            }

            var tenantName = Environment.GetEnvironmentVariable("TEMP_BYPASS_TENANT_NAME")?.Trim();
            var adminEmail = Environment.GetEnvironmentVariable("TEMP_BYPASS_TENANT_ADMIN_EMAIL")?.Trim().ToLowerInvariant();
            var adminPassword = Environment.GetEnvironmentVariable("TEMP_BYPASS_TENANT_ADMIN_PASSWORD");
            var firstName = Environment.GetEnvironmentVariable("TEMP_BYPASS_TENANT_ADMIN_FIRST_NAME")?.Trim();
            var lastName = Environment.GetEnvironmentVariable("TEMP_BYPASS_TENANT_ADMIN_LAST_NAME")?.Trim();
            var tenantTemplate = Environment.GetEnvironmentVariable("TENANT_DB_TEMPLATE") ?? Environment.GetEnvironmentVariable("TENANT_DB_DEFAULT");

            if (string.IsNullOrWhiteSpace(tenantName) ||
                string.IsNullOrWhiteSpace(adminEmail) ||
                string.IsNullOrWhiteSpace(adminPassword))
            {
                throw new InvalidOperationException(
                    "TEMP_BYPASS_SEED_TENANT_ADMIN is enabled, but tenant/admin seed variables are incomplete.");
            }

            if (string.IsNullOrWhiteSpace(tenantTemplate))
            {
                throw new InvalidOperationException(
                    "TEMP_BYPASS_SEED_TENANT_ADMIN is enabled, but TENANT_DB_TEMPLATE is not configured.");
            }

            var hasher = new Services.Sha256PasswordHasher();

            var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Name == tenantName);
            if (tenant == null)
            {
                tenant = new Tenant
                {
                    TenantId = Guid.NewGuid(),
                    Name = tenantName,
                    Status = TenantStatus.Active,
                    CreatedAt = now,
                    ActivatedAt = now
                };
                db.Tenants.Add(tenant);
            }
            else
            {
                tenant.Status = TenantStatus.Active;
                tenant.ActivatedAt ??= now;
            }

            if (db.Entry(tenant).State == EntityState.Added)
            {
                // Persist the tenant first so subscription inserts do not form a circular dependency.
                await db.SaveChangesAsync();
            }

            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == adminEmail);
            if (user == null)
            {
                user = new User
                {
                    UserId = Guid.NewGuid(),
                    Email = adminEmail,
                    DisplayName = adminEmail,
                    FirstName = string.IsNullOrWhiteSpace(firstName) ? "Tenant" : firstName,
                    LastName = string.IsNullOrWhiteSpace(lastName) ? "Admin" : lastName,
                    Country = string.Empty,
                    Gender = string.Empty,
                    PasswordHash = hasher.Hash(adminPassword),
                    CreatedAt = now,
                    UpdatedAt = now,
                    EmailConfirmed = true,
                    IsActive = true,
                    Status = UserStatus.Active
                };
                db.Users.Add(user);
            }
            else
            {
                user.PasswordHash = hasher.Hash(adminPassword);
                user.IsActive = true;
                user.EmailConfirmed = true;
                user.Status = UserStatus.Active;
                user.UpdatedAt = now;
                user.FirstName = string.IsNullOrWhiteSpace(user.FirstName) ? (string.IsNullOrWhiteSpace(firstName) ? "Tenant" : firstName) : user.FirstName;
                user.LastName = string.IsNullOrWhiteSpace(user.LastName) ? (string.IsNullOrWhiteSpace(lastName) ? "Admin" : lastName) : user.LastName;
            }

            var roleAssigned = await db.UserRoles.AnyAsync(ur => ur.UserId == user.UserId && ur.RoleId == userRoleId);
            if (!roleAssigned)
            {
                db.UserRoles.Add(new UserRole
                {
                    UserId = user.UserId,
                    RoleId = userRoleId,
                    AssignedAt = now
                });
            }

            var plan = await db.Plans.FirstOrDefaultAsync(p => p.Code == "TEMP-SEED-5-USERS");
            if (plan == null)
            {
                plan = new Plan
                {
                    PlanId = Guid.NewGuid(),
                    Code = "TEMP-SEED-5-USERS",
                    Name = "Temporary Seed Plan (5 users)",
                    PriceCents = 0,
                    Currency = "USD",
                    DurationDays = 3650,
                    MaxAccounts = 5,
                    IsActive = true,
                    CreatedAt = now
                };
                db.Plans.Add(plan);
            }
            else
            {
                plan.MaxAccounts = 5;
                plan.IsActive = true;
            }

            var subscription = await db.Subscriptions
                .FirstOrDefaultAsync(s => s.TenantId == tenant.TenantId && s.Status == SubscriptionStatus.Active);
            if (subscription == null)
            {
                subscription = new Subscription
                {
                    SubscriptionId = Guid.NewGuid(),
                    TenantId = tenant.TenantId,
                    PlanId = plan.PlanId,
                    Status = SubscriptionStatus.Active,
                    CurrentPeriodStart = now,
                    CurrentPeriodEnd = now.AddDays(plan.DurationDays),
                    CreatedAt = now
                };
                db.Subscriptions.Add(subscription);
            }
            else
            {
                subscription.PlanId = plan.PlanId;
                subscription.Status = SubscriptionStatus.Active;
                subscription.CurrentPeriodStart ??= now;
                subscription.CurrentPeriodEnd ??= now.AddDays(plan.DurationDays);
            }

            if (db.Entry(subscription).State == EntityState.Added)
            {
                await db.SaveChangesAsync();
            }

            tenant.CurrentSubscriptionId = subscription.SubscriptionId;

            var tenantAdminLink = await db.TenantUsers
                .FirstOrDefaultAsync(tu => tu.TenantId == tenant.TenantId && tu.UserId == user.UserId);
            if (tenantAdminLink == null)
            {
                db.TenantUsers.Add(new TenantUser
                {
                    TenantUserId = Guid.NewGuid(),
                    TenantId = tenant.TenantId,
                    UserId = user.UserId,
                    Role = TenantUserRole.TenantAdmin,
                    CreatedAt = now
                });
            }
            else
            {
                tenantAdminLink.Role = TenantUserRole.TenantAdmin;
            }

            var tenantConnectionString = tenantTemplate.Replace("{tenantId}", tenant.TenantId.ToString(), StringComparison.OrdinalIgnoreCase);
            await EnsureTenantDatabasePhysicalAsync(tenantConnectionString);
            await EnsureTenantUserSchemaAsync(tenantConnectionString);
            await UpsertTenantScopedAdminAsync(tenantConnectionString, user, now);
            await EnsureTenantAdminRoleAssignmentAsync(tenantConnectionString, user.UserId, now);

            var protector = DataProtectionProvider.Create("finrecon360-backend").CreateProtector("finrecon360.tenant-db");
            var encryptedConnection = protector.Protect(tenantConnectionString);
            var tenantDbRecord = await db.TenantDatabases
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync(x => x.TenantId == tenant.TenantId);

            if (tenantDbRecord == null)
            {
                db.TenantDatabases.Add(new TenantDatabase
                {
                    TenantDatabaseId = Guid.NewGuid(),
                    TenantId = tenant.TenantId,
                    EncryptedConnectionString = encryptedConnection,
                    Provider = "SqlServer",
                    Status = TenantDatabaseStatus.Ready,
                    CreatedAt = now,
                    ProvisionedAt = now
                });
            }
            else
            {
                tenantDbRecord.EncryptedConnectionString = encryptedConnection;
                tenantDbRecord.Provider = "SqlServer";
                tenantDbRecord.Status = TenantDatabaseStatus.Ready;
                tenantDbRecord.ProvisionedAt ??= now;
            }
        }

        private static async Task EnsureTenantDatabasePhysicalAsync(string tenantConnectionString)
        {
            var tenantBuilder = new SqlConnectionStringBuilder(tenantConnectionString);
            var databaseName = tenantBuilder.InitialCatalog;
            if (string.IsNullOrWhiteSpace(databaseName))
            {
                throw new InvalidOperationException("TENANT_DB_TEMPLATE must include a database name.");
            }

            var masterBuilder = new SqlConnectionStringBuilder(tenantConnectionString)
            {
                InitialCatalog = "master"
            };

            await using var connection = new SqlConnection(masterBuilder.ConnectionString);
            await connection.OpenAsync();
            var sql = $"""
                IF DB_ID(N'{databaseName.Replace("'", "''")}') IS NULL
                BEGIN
                    CREATE DATABASE [{databaseName.Replace("]", "]]")}];
                END
                """;
            await using var command = new SqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }

        private static async Task EnsureTenantUserSchemaAsync(string tenantConnectionString)
        {
            await using var connection = new SqlConnection(tenantConnectionString);
            await connection.OpenAsync();
            var sql = """
                IF OBJECT_ID(N'dbo.__TenantSchemaMigrations', N'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.__TenantSchemaMigrations (
                        MigrationId nvarchar(150) NOT NULL PRIMARY KEY,
                        AppliedAt datetime2 NOT NULL CONSTRAINT DF_TenantSchemaMigrations_AppliedAt DEFAULT SYSUTCDATETIME()
                    );
                END

                IF OBJECT_ID(N'dbo.TenantUsers', N'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.TenantUsers (
                        TenantUserId uniqueidentifier NOT NULL PRIMARY KEY,
                        UserId uniqueidentifier NOT NULL,
                        Email nvarchar(256) NOT NULL,
                        DisplayName nvarchar(256) NULL,
                        Role nvarchar(32) NOT NULL,
                        Status nvarchar(32) NOT NULL,
                        IsActive bit NOT NULL CONSTRAINT DF_TenantUsers_IsActive DEFAULT (1),
                        CreatedAt datetime2 NOT NULL CONSTRAINT DF_TenantUsers_CreatedAt DEFAULT SYSUTCDATETIME(),
                        UpdatedAt datetime2 NULL
                    );

                    CREATE UNIQUE INDEX IX_TenantUsers_UserId ON dbo.TenantUsers (UserId);
                    CREATE INDEX IX_TenantUsers_Email ON dbo.TenantUsers (Email);
                END
                """;
            await using var command = new SqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }

        private static async Task UpsertTenantScopedAdminAsync(string tenantConnectionString, User user, DateTime now)
        {
            await using var connection = new SqlConnection(tenantConnectionString);
            await connection.OpenAsync();
            var sql = """
                IF EXISTS (SELECT 1 FROM dbo.TenantUsers WHERE UserId = @userId)
                BEGIN
                    UPDATE dbo.TenantUsers
                    SET Email = @email,
                        DisplayName = @displayName,
                        Role = @role,
                        Status = @status,
                        IsActive = @isActive,
                        UpdatedAt = @updatedAt
                    WHERE UserId = @userId;
                END
                ELSE
                BEGIN
                    INSERT INTO dbo.TenantUsers
                        (TenantUserId, UserId, Email, DisplayName, Role, Status, IsActive, CreatedAt, UpdatedAt)
                    VALUES
                        (@tenantUserId, @userId, @email, @displayName, @role, @status, @isActive, @createdAt, @updatedAt);
                END
                """;

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@tenantUserId", Guid.NewGuid());
            command.Parameters.AddWithValue("@userId", user.UserId);
            command.Parameters.AddWithValue("@email", user.Email);
            command.Parameters.AddWithValue("@displayName", (object?)user.DisplayName ?? $"{user.FirstName} {user.LastName}".Trim());
            command.Parameters.AddWithValue("@role", TenantUserRole.TenantAdmin.ToString());
            command.Parameters.AddWithValue("@status", user.Status.ToString());
            command.Parameters.AddWithValue("@isActive", user.IsActive);
            command.Parameters.AddWithValue("@createdAt", now);
            command.Parameters.AddWithValue("@updatedAt", now);
            await command.ExecuteNonQueryAsync();
        }

        private static async Task EnsureTenantAdminRoleAssignmentAsync(string tenantConnectionString, Guid userId, DateTime now)
        {
            await using var connection = new SqlConnection(tenantConnectionString);
            await connection.OpenAsync();
            var sql = """
                DECLARE @adminRoleId uniqueidentifier = (
                    SELECT TOP(1) RoleId FROM dbo.Roles WHERE Code = N'ADMIN' AND IsActive = 1
                );

                IF @adminRoleId IS NOT NULL
                BEGIN
                    DELETE FROM dbo.UserRoles WHERE UserId = @userId;

                    INSERT INTO dbo.UserRoles (UserId, RoleId, AssignedAt)
                    VALUES (@userId, @adminRoleId, @assignedAt);
                END
                """;

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@userId", userId);
            command.Parameters.AddWithValue("@assignedAt", now);
            await command.ExecuteNonQueryAsync();
        }

        private static async Task<bool> TableExistsAsync(AppDbContext db, string tableName)
        {
            var connection = db.Database.GetDbConnection();
            var shouldCloseConnection = connection.State != ConnectionState.Open;
            if (shouldCloseConnection)
            {
                await connection.OpenAsync();
            }

            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT TOP(1) 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = @tableName";
                var parameter = command.CreateParameter();
                parameter.ParameterName = "@tableName";
                parameter.Value = tableName;
                command.Parameters.Add(parameter);
                var result = await command.ExecuteScalarAsync();
                return result is not null;
            }
            finally
            {
                if (shouldCloseConnection)
                {
                    await connection.CloseAsync();
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
                ,
                ("TENANT_REG_MGMT", "Tenant Registrations", "/app/admin/tenant-registrations", "Admin", "Tenant registration approvals"),
                ("TENANT_MGMT", "Tenants", "/app/admin/tenants", "Admin", "Tenant management"),
                ("PLAN_MGMT", "Subscription Plans", "/app/admin/plans", "Admin", "Subscription plans"),
                ("ENFORCEMENT_MGMT", "Enforcement", "/app/admin/enforcement", "Admin", "Suspensions and bans")
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
