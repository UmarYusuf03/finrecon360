using Microsoft.Data.SqlClient;

namespace finrecon360_backend.Services
{
    public interface ITenantSchemaMigrator
    {
        Task ApplyAsync(string tenantConnectionString, CancellationToken cancellationToken = default);
    }

    public class SqlServerTenantSchemaMigrator : ITenantSchemaMigrator
    {
        private const string MigrationInitial = "202603010001_InitialTenantSchema";
        private const string MigrationRbac = "202603020001_TenantRbacSchema";
        private const string MigrationRbacReconcile = "202603050001_TenantRbacReconcile";
        private const string SchemaLockResource = "finrecon360:tenant-schema-migrator";

        public async Task ApplyAsync(string tenantConnectionString, CancellationToken cancellationToken = default)
        {
            await using var connection = new SqlConnection(tenantConnectionString);
            await connection.OpenAsync(cancellationToken);
            await AcquireSchemaLockAsync(connection, cancellationToken);

            await EnsureMigrationsTableAsync(connection, cancellationToken);
            await ApplyMigrationIfMissingAsync(connection, MigrationInitial, BuildInitialSql(), cancellationToken);
            await ApplyMigrationIfMissingAsync(connection, MigrationRbac, BuildTenantRbacSql(), cancellationToken);
            await ApplyMigrationIfMissingAsync(connection, MigrationRbacReconcile, BuildTenantRbacReconcileSql(), cancellationToken);
        }

        private static async Task AcquireSchemaLockAsync(SqlConnection connection, CancellationToken cancellationToken)
        {
            await using var command = new SqlCommand(
                """
                DECLARE @result int;
                EXEC @result = sp_getapplock
                    @Resource = @resource,
                    @LockMode = 'Exclusive',
                    @LockOwner = 'Session',
                    @LockTimeout = 15000;
                SELECT @result;
                """,
                connection);
            command.Parameters.AddWithValue("@resource", SchemaLockResource);

            var result = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken) ?? -999);
            if (result < 0)
            {
                throw new InvalidOperationException($"Failed to acquire tenant schema lock. sp_getapplock returned {result}.");
            }
        }

        private static async Task EnsureMigrationsTableAsync(SqlConnection connection, CancellationToken cancellationToken)
        {
            var sql = """
                IF OBJECT_ID(N'dbo.__TenantSchemaMigrations', N'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.__TenantSchemaMigrations (
                        MigrationId nvarchar(150) NOT NULL PRIMARY KEY,
                        AppliedAt datetime2 NOT NULL CONSTRAINT DF_TenantSchemaMigrations_AppliedAt DEFAULT SYSUTCDATETIME()
                    );
                END
                """;

            await using var command = new SqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        private static async Task ApplyMigrationIfMissingAsync(
            SqlConnection connection,
            string migrationId,
            string migrationSql,
            CancellationToken cancellationToken)
        {
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
            try
            {
                await using var existsCommand = new SqlCommand(
                    "SELECT COUNT(1) FROM dbo.__TenantSchemaMigrations WITH (UPDLOCK, HOLDLOCK) WHERE MigrationId = @migrationId",
                    connection,
                    transaction);
                existsCommand.Parameters.AddWithValue("@migrationId", migrationId);
                var exists = (int)(await existsCommand.ExecuteScalarAsync(cancellationToken) ?? 0);
                if (exists > 0)
                {
                    await transaction.CommitAsync(cancellationToken);
                    return;
                }

                await ExecuteNonQueryAsync(connection, transaction, migrationSql, cancellationToken);
                await ExecuteNonQueryAsync(
                    connection,
                    transaction,
                    "INSERT INTO dbo.__TenantSchemaMigrations (MigrationId) VALUES (@migrationId)",
                    cancellationToken,
                    new SqlParameter("@migrationId", migrationId));

                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }

        private static string BuildInitialSql() =>
            """
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

        private static string BuildTenantRbacSql() =>
            """
            IF OBJECT_ID(N'dbo.Roles', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.Roles (
                    RoleId uniqueidentifier NOT NULL PRIMARY KEY,
                    Code nvarchar(100) NOT NULL,
                    Name nvarchar(150) NOT NULL,
                    Description nvarchar(500) NULL,
                    IsSystem bit NOT NULL CONSTRAINT DF_TenantRoles_IsSystem DEFAULT (0),
                    IsActive bit NOT NULL CONSTRAINT DF_TenantRoles_IsActive DEFAULT (1),
                    CreatedAt datetime2 NOT NULL CONSTRAINT DF_TenantRoles_CreatedAt DEFAULT SYSUTCDATETIME()
                );
                CREATE UNIQUE INDEX IX_TenantRoles_Code ON dbo.Roles(Code);
            END

            IF OBJECT_ID(N'dbo.Permissions', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.Permissions (
                    PermissionId uniqueidentifier NOT NULL PRIMARY KEY,
                    Code nvarchar(150) NOT NULL,
                    Name nvarchar(200) NOT NULL,
                    Description nvarchar(500) NULL,
                    Module nvarchar(100) NULL,
                    CreatedAt datetime2 NOT NULL CONSTRAINT DF_TenantPermissions_CreatedAt DEFAULT SYSUTCDATETIME()
                );
                CREATE UNIQUE INDEX IX_TenantPermissions_Code ON dbo.Permissions(Code);
            END

            IF OBJECT_ID(N'dbo.RolePermissions', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.RolePermissions (
                    RoleId uniqueidentifier NOT NULL,
                    PermissionId uniqueidentifier NOT NULL,
                    GrantedAt datetime2 NOT NULL CONSTRAINT DF_TenantRolePermissions_GrantedAt DEFAULT SYSUTCDATETIME(),
                    CONSTRAINT PK_TenantRolePermissions PRIMARY KEY (RoleId, PermissionId),
                    CONSTRAINT FK_TenantRolePermissions_Roles_RoleId FOREIGN KEY (RoleId) REFERENCES dbo.Roles(RoleId) ON DELETE CASCADE,
                    CONSTRAINT FK_TenantRolePermissions_Permissions_PermissionId FOREIGN KEY (PermissionId) REFERENCES dbo.Permissions(PermissionId) ON DELETE CASCADE
                );
            END

            IF OBJECT_ID(N'dbo.AppComponents', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.AppComponents (
                    ComponentId uniqueidentifier NOT NULL PRIMARY KEY,
                    Code nvarchar(100) NOT NULL,
                    Name nvarchar(200) NOT NULL,
                    RoutePath nvarchar(200) NOT NULL,
                    Category nvarchar(100) NULL,
                    Description nvarchar(500) NULL,
                    IsActive bit NOT NULL CONSTRAINT DF_TenantComponents_IsActive DEFAULT (1),
                    CreatedAt datetime2 NOT NULL CONSTRAINT DF_TenantComponents_CreatedAt DEFAULT SYSUTCDATETIME()
                );
                CREATE UNIQUE INDEX IX_TenantComponents_Code ON dbo.AppComponents(Code);
            END

            IF OBJECT_ID(N'dbo.PermissionActions', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.PermissionActions (
                    PermissionActionId uniqueidentifier NOT NULL PRIMARY KEY,
                    Code nvarchar(50) NOT NULL,
                    Name nvarchar(100) NOT NULL,
                    Description nvarchar(300) NULL,
                    IsActive bit NOT NULL CONSTRAINT DF_TenantPermissionActions_IsActive DEFAULT (1),
                    CreatedAt datetime2 NOT NULL CONSTRAINT DF_TenantPermissionActions_CreatedAt DEFAULT SYSUTCDATETIME()
                );
                CREATE UNIQUE INDEX IX_TenantPermissionActions_Code ON dbo.PermissionActions(Code);
            END

            IF OBJECT_ID(N'dbo.UserRoles', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.UserRoles (
                    UserId uniqueidentifier NOT NULL,
                    RoleId uniqueidentifier NOT NULL,
                    AssignedAt datetime2 NOT NULL CONSTRAINT DF_TenantUserRoles_AssignedAt DEFAULT SYSUTCDATETIME(),
                    CONSTRAINT PK_TenantUserRoles PRIMARY KEY (UserId, RoleId),
                    CONSTRAINT FK_TenantUserRoles_Roles_RoleId FOREIGN KEY (RoleId) REFERENCES dbo.Roles(RoleId) ON DELETE CASCADE
                );
                CREATE INDEX IX_TenantUserRoles_RoleId ON dbo.UserRoles(RoleId);
            END

            IF NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE Code = N'ADMIN')
            BEGIN
                INSERT INTO dbo.Roles (RoleId, Code, Name, Description, IsSystem, IsActive)
                VALUES (NEWID(), N'ADMIN', N'Tenant Administrator', N'Tenant-level administrator', 1, 1);
            END

            IF NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE Code = N'USER')
            BEGIN
                INSERT INTO dbo.Roles (RoleId, Code, Name, Description, IsSystem, IsActive)
                VALUES (NEWID(), N'USER', N'Tenant User', N'Standard tenant user', 1, 1);
            END

            IF NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE Code = N'MANAGER')
            BEGIN
                INSERT INTO dbo.Roles (RoleId, Code, Name, Description, IsSystem, IsActive)
                VALUES (NEWID(), N'MANAGER', N'Tenant Manager', N'Operational manager with broad non-system access', 1, 1);
            END

            IF NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE Code = N'REVIEWER')
            BEGIN
                INSERT INTO dbo.Roles (RoleId, Code, Name, Description, IsSystem, IsActive)
                VALUES (NEWID(), N'REVIEWER', N'Reviewer', N'Read-focused reviewer role', 1, 1);
            END

            UPDATE dbo.Roles
            SET IsSystem = 1
            WHERE Code IN (N'ADMIN', N'MANAGER', N'REVIEWER', N'USER') AND IsSystem = 0;

            INSERT INTO dbo.Permissions (PermissionId, Code, Name, Description, Module)
            SELECT NEWID(), v.Code, v.Name, v.Description, v.Module
            FROM (VALUES
                (N'ADMIN.DASHBOARD.VIEW', N'Dashboard', N'View admin dashboard', N'Admin'),
                (N'ADMIN.USERS.VIEW', N'User Management View', N'View tenant users', N'Admin'),
                (N'ADMIN.USERS.CREATE', N'User Management Create', N'Create tenant users', N'Admin'),
                (N'ADMIN.USERS.EDIT', N'User Management Edit', N'Edit tenant users', N'Admin'),
                (N'ADMIN.USERS.DELETE', N'User Management Delete', N'Deactivate tenant users', N'Admin'),
                (N'ADMIN.USERS.MANAGE', N'User Management Manage', N'Manage tenant users', N'Admin'),
                (N'ADMIN.ROLES.VIEW', N'Role Management View', N'View tenant roles', N'Admin'),
                (N'ADMIN.ROLES.CREATE', N'Role Management Create', N'Create tenant roles', N'Admin'),
                (N'ADMIN.ROLES.EDIT', N'Role Management Edit', N'Edit tenant roles', N'Admin'),
                (N'ADMIN.ROLES.DELETE', N'Role Management Delete', N'Deactivate tenant roles', N'Admin'),
                (N'ADMIN.ROLES.MANAGE', N'Role Management Manage', N'Manage tenant roles', N'Admin'),
                (N'ADMIN.PERMISSIONS.VIEW', N'Permission Management View', N'View tenant permissions', N'Admin'),
                (N'ADMIN.PERMISSIONS.CREATE', N'Permission Management Create', N'Create tenant permissions', N'Admin'),
                (N'ADMIN.PERMISSIONS.EDIT', N'Permission Management Edit', N'Edit tenant permissions', N'Admin'),
                (N'ADMIN.PERMISSIONS.DELETE', N'Permission Management Delete', N'Delete tenant permissions', N'Admin'),
                (N'ADMIN.PERMISSIONS.MANAGE', N'Permission Management Manage', N'Manage tenant permissions', N'Admin'),
                (N'ADMIN.COMPONENTS.VIEW', N'Component Management View', N'View tenant components', N'Admin'),
                (N'ADMIN.COMPONENTS.CREATE', N'Component Management Create', N'Create tenant components', N'Admin'),
                (N'ADMIN.COMPONENTS.EDIT', N'Component Management Edit', N'Edit tenant components', N'Admin'),
                (N'ADMIN.COMPONENTS.DELETE', N'Component Management Delete', N'Deactivate tenant components', N'Admin'),
                (N'ADMIN.COMPONENTS.MANAGE', N'Component Management Manage', N'Manage tenant components', N'Admin'),
                (N'MATCHER.VIEW', N'Matcher View', N'View matcher', N'Reconciliation'),
                (N'MATCHER.MANAGE', N'Matcher Manage', N'Manage matcher', N'Reconciliation'),
                (N'BALANCER.VIEW', N'Balancer View', N'View balancer', N'Reconciliation'),
                (N'BALANCER.MANAGE', N'Balancer Manage', N'Manage balancer', N'Reconciliation'),
                (N'TASKS.VIEW', N'Tasks View', N'View tasks', N'Reconciliation'),
                (N'JOURNAL.VIEW', N'Journal View', N'View journal', N'Accounting'),
                (N'ANALYTICS.VIEW', N'Analytics View', N'View analytics', N'Analytics')
            ) v(Code, Name, Description, Module)
            WHERE NOT EXISTS (SELECT 1 FROM dbo.Permissions p WHERE p.Code = v.Code);

            INSERT INTO dbo.PermissionActions (PermissionActionId, Code, Name, Description, IsActive)
            SELECT NEWID(), v.Code, v.Name, v.Description, 1
            FROM (VALUES
                (N'VIEW', N'View', N'Read access'),
                (N'VIEW_LIST', N'View List', N'List access'),
                (N'CREATE', N'Create', N'Create access'),
                (N'EDIT', N'Edit', N'Edit access'),
                (N'DELETE', N'Delete', N'Delete access'),
                (N'APPROVE', N'Approve', N'Approve access'),
                (N'MANAGE', N'Manage', N'Manage access')
            ) v(Code, Name, Description)
            WHERE NOT EXISTS (SELECT 1 FROM dbo.PermissionActions a WHERE a.Code = v.Code);

            INSERT INTO dbo.AppComponents (ComponentId, Code, Name, RoutePath, Category, Description, IsActive)
            SELECT NEWID(), v.Code, v.Name, v.RoutePath, v.Category, v.Description, 1
            FROM (VALUES
                (N'DASHBOARD', N'Dashboard', N'/app/dashboard', N'Analytics', N'Tenant dashboard'),
                (N'USER_MGMT', N'User Management', N'/app/admin/users', N'Admin', N'Tenant users'),
                (N'ROLE_MGMT', N'Role Management', N'/app/admin/roles', N'Admin', N'Tenant roles'),
                (N'COMPONENT_MGMT', N'Component Management', N'/app/admin/components', N'Admin', N'Tenant components'),
                (N'PERMISSION_MGMT', N'Permission Management', N'/app/admin/permissions', N'Admin', N'Tenant permissions')
            ) v(Code, Name, RoutePath, Category, Description)
            WHERE NOT EXISTS (SELECT 1 FROM dbo.AppComponents c WHERE c.Code = v.Code);

            INSERT INTO dbo.RolePermissions (RoleId, PermissionId)
            SELECT r.RoleId, p.PermissionId
            FROM dbo.Roles r
            CROSS JOIN dbo.Permissions p
            WHERE r.Code = N'ADMIN'
              AND NOT EXISTS (
                  SELECT 1 FROM dbo.RolePermissions rp
                  WHERE rp.RoleId = r.RoleId AND rp.PermissionId = p.PermissionId
              );

            INSERT INTO dbo.RolePermissions (RoleId, PermissionId)
            SELECT r.RoleId, p.PermissionId
            FROM dbo.Roles r
            INNER JOIN (VALUES
                (N'ADMIN.DASHBOARD.VIEW'),
                (N'ADMIN.USERS.VIEW'),
                (N'ADMIN.USERS.CREATE'),
                (N'ADMIN.USERS.EDIT'),
                (N'ADMIN.ROLES.VIEW'),
                (N'ADMIN.PERMISSIONS.VIEW'),
                (N'ADMIN.COMPONENTS.VIEW'),
                (N'MATCHER.VIEW'),
                (N'BALANCER.VIEW'),
                (N'TASKS.VIEW'),
                (N'JOURNAL.VIEW'),
                (N'ANALYTICS.VIEW')
            ) allowed(Code) ON 1 = 1
            INNER JOIN dbo.Permissions p ON p.Code = allowed.Code
            WHERE r.Code = N'MANAGER'
              AND NOT EXISTS (
                  SELECT 1 FROM dbo.RolePermissions rp
                  WHERE rp.RoleId = r.RoleId AND rp.PermissionId = p.PermissionId
              );

            INSERT INTO dbo.RolePermissions (RoleId, PermissionId)
            SELECT r.RoleId, p.PermissionId
            FROM dbo.Roles r
            INNER JOIN (VALUES
                (N'ADMIN.DASHBOARD.VIEW'),
                (N'MATCHER.VIEW'),
                (N'BALANCER.VIEW'),
                (N'TASKS.VIEW'),
                (N'JOURNAL.VIEW'),
                (N'ANALYTICS.VIEW')
            ) allowed(Code) ON 1 = 1
            INNER JOIN dbo.Permissions p ON p.Code = allowed.Code
            WHERE r.Code = N'REVIEWER'
              AND NOT EXISTS (
                  SELECT 1 FROM dbo.RolePermissions rp
                  WHERE rp.RoleId = r.RoleId AND rp.PermissionId = p.PermissionId
              );

            INSERT INTO dbo.RolePermissions (RoleId, PermissionId)
            SELECT r.RoleId, p.PermissionId
            FROM dbo.Roles r
            INNER JOIN (VALUES
                (N'MATCHER.VIEW'),
                (N'BALANCER.VIEW'),
                (N'TASKS.VIEW'),
                (N'JOURNAL.VIEW'),
                (N'ANALYTICS.VIEW')
            ) allowed(Code) ON 1 = 1
            INNER JOIN dbo.Permissions p ON p.Code = allowed.Code
            WHERE r.Code = N'USER'
              AND NOT EXISTS (
                  SELECT 1 FROM dbo.RolePermissions rp
                  WHERE rp.RoleId = r.RoleId AND rp.PermissionId = p.PermissionId
              );
            """;

        private static string BuildTenantRbacReconcileSql() =>
            """
            IF NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE Code = N'ADMIN')
            BEGIN
                INSERT INTO dbo.Roles (RoleId, Code, Name, Description, IsSystem, IsActive)
                VALUES (NEWID(), N'ADMIN', N'Tenant Administrator', N'Tenant-level administrator', 1, 1);
            END

            IF NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE Code = N'USER')
            BEGIN
                INSERT INTO dbo.Roles (RoleId, Code, Name, Description, IsSystem, IsActive)
                VALUES (NEWID(), N'USER', N'Tenant User', N'Standard tenant user', 1, 1);
            END

            IF NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE Code = N'MANAGER')
            BEGIN
                INSERT INTO dbo.Roles (RoleId, Code, Name, Description, IsSystem, IsActive)
                VALUES (NEWID(), N'MANAGER', N'Tenant Manager', N'Operational manager with broad non-system access', 1, 1);
            END

            IF NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE Code = N'REVIEWER')
            BEGIN
                INSERT INTO dbo.Roles (RoleId, Code, Name, Description, IsSystem, IsActive)
                VALUES (NEWID(), N'REVIEWER', N'Reviewer', N'Read-focused reviewer role', 1, 1);
            END

            UPDATE dbo.Roles
            SET IsSystem = 1
            WHERE Code IN (N'ADMIN', N'MANAGER', N'REVIEWER', N'USER') AND IsSystem = 0;
            """;

        private static async Task ExecuteNonQueryAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            string sql,
            CancellationToken cancellationToken,
            params SqlParameter[] parameters)
        {
            await using var command = new SqlCommand(sql, connection, transaction);
            if (parameters.Length > 0)
            {
                command.Parameters.AddRange(parameters);
            }

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
