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
        private const string MigrationImportArchitecture = "202604090001_TenantImportArchitectureFoundation";
        private const string MigrationImportBatchMappingLink = "202604100001_TenantImportBatchMappingLink";
        private const string MigrationImportWorkbenchPermissions = "202604270001_TenantImportWorkbenchPermissions";
        private const string MigrationBankAccounts = "202604230001_TenantBankAccounts";
        private const string MigrationBankAccountsPermissions = "202604230002_TenantBankAccountsPermissions";
        private const string MigrationTransactions = "202604230003_TenantTransactions";
        private const string MigrationTransactionPermissions = "202604230004_TenantTransactionPermissions";
        private const string MigrationTransactionApprovalFields = "202604230005_TenantTransactionApprovalFields";
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
            await ApplyMigrationIfMissingAsync(connection, MigrationImportArchitecture, BuildTenantImportArchitectureSql(), cancellationToken);
            await ApplyMigrationIfMissingAsync(connection, MigrationImportBatchMappingLink, BuildTenantImportBatchMappingLinkSql(), cancellationToken);
            await ApplyMigrationIfMissingAsync(connection, MigrationImportWorkbenchPermissions, BuildTenantImportWorkbenchPermissionsSql(), cancellationToken);
            await ApplyMigrationIfMissingAsync(connection, MigrationBankAccounts, BuildTenantBankAccountsSql(), cancellationToken);
            await ApplyMigrationIfMissingAsync(connection, MigrationBankAccountsPermissions, BuildTenantBankAccountsPermissionsSql(), cancellationToken);
            await ApplyMigrationIfMissingAsync(connection, MigrationTransactions, BuildTenantTransactionsSql(), cancellationToken);
            await ApplyMigrationIfMissingAsync(connection, MigrationTransactionPermissions, BuildTenantTransactionPermissionsSql(), cancellationToken);
            await ApplyMigrationIfMissingAsync(connection, MigrationTransactionApprovalFields, BuildTenantTransactionApprovalFieldsSql(), cancellationToken);
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
                (N'ADMIN.IMPORT_WORKBENCH.VIEW', N'Import Workbench View', N'View import workbench', N'Admin'),
                (N'ADMIN.IMPORT_ARCHITECTURE.VIEW', N'Import Architecture View', N'View import architecture foundation', N'Admin'),
                (N'ADMIN.IMPORT_ARCHITECTURE.MANAGE', N'Import Architecture Manage', N'Manage import architecture templates and metadata', N'Admin'),
                (N'ADMIN.AUDIT_LOGS.VIEW', N'Audit Logs View', N'View tenant audit logs', N'Admin'),
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
                (N'PERMISSION_MGMT', N'Permission Management', N'/app/admin/permissions', N'Admin', N'Tenant permissions'),
                                (N'IMPORT_WORKBENCH_MGMT', N'Import Workbench', N'/app/imports/workbench', N'Admin', N'Tenant imports processing workspace'),
                                (N'IMPORT_ARCHITECTURE_MGMT', N'Import Architecture', N'/app/imports/import-architecture', N'Admin', N'Tenant import foundation and canonical schema'),
                                (N'AUDIT_LOGS_MGMT', N'Audit Logs', N'/app/admin/audit-logs', N'Admin', N'Tenant audit logs')
            ) v(Code, Name, RoutePath, Category, Description)
            WHERE NOT EXISTS (SELECT 1 FROM dbo.AppComponents c WHERE c.Code = v.Code);

                        UPDATE dbo.AppComponents
                        SET RoutePath = N'/app/imports/workbench'
                        WHERE Code = N'IMPORT_WORKBENCH_MGMT'
                            AND RoutePath <> N'/app/imports/workbench';

                        UPDATE dbo.AppComponents
                        SET RoutePath = N'/app/imports/import-architecture'
                        WHERE Code = N'IMPORT_ARCHITECTURE_MGMT'
                            AND RoutePath <> N'/app/imports/import-architecture';

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
                (N'ADMIN.IMPORT_WORKBENCH.VIEW'),
                (N'ADMIN.AUDIT_LOGS.VIEW'),
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

        private static string BuildTenantImportArchitectureSql() =>
            """
            IF OBJECT_ID(N'dbo.ImportBatches', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.ImportBatches (
                    ImportBatchId uniqueidentifier NOT NULL PRIMARY KEY,
                    SourceType nvarchar(100) NOT NULL,
                    Status nvarchar(50) NOT NULL,
                    ImportedAt datetime2 NOT NULL CONSTRAINT DF_ImportBatches_ImportedAt DEFAULT SYSUTCDATETIME(),
                    UploadedByUserId uniqueidentifier NULL,
                    OriginalFileName nvarchar(260) NULL,
                    RawRecordCount int NOT NULL CONSTRAINT DF_ImportBatches_RawRecordCount DEFAULT (0),
                    NormalizedRecordCount int NOT NULL CONSTRAINT DF_ImportBatches_NormalizedRecordCount DEFAULT (0),
                    ErrorMessage nvarchar(1000) NULL
                );

                CREATE INDEX IX_ImportBatches_ImportedAt ON dbo.ImportBatches(ImportedAt);
                CREATE INDEX IX_ImportBatches_SourceType_Status ON dbo.ImportBatches(SourceType, Status);
            END

            IF OBJECT_ID(N'dbo.ImportedRawRecords', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.ImportedRawRecords (
                    ImportedRawRecordId uniqueidentifier NOT NULL PRIMARY KEY,
                    ImportBatchId uniqueidentifier NOT NULL,
                    RowNumber int NULL,
                    SourcePayloadJson nvarchar(max) NOT NULL,
                    NormalizationStatus nvarchar(50) NOT NULL CONSTRAINT DF_ImportedRawRecords_NormalizationStatus DEFAULT (N'PENDING'),
                    NormalizationErrors nvarchar(2000) NULL,
                    CreatedAt datetime2 NOT NULL CONSTRAINT DF_ImportedRawRecords_CreatedAt DEFAULT SYSUTCDATETIME(),
                    CONSTRAINT FK_ImportedRawRecords_ImportBatches_ImportBatchId FOREIGN KEY (ImportBatchId) REFERENCES dbo.ImportBatches(ImportBatchId) ON DELETE CASCADE
                );

                CREATE INDEX IX_ImportedRawRecords_ImportBatchId ON dbo.ImportedRawRecords(ImportBatchId);
                CREATE INDEX IX_ImportedRawRecords_CreatedAt ON dbo.ImportedRawRecords(CreatedAt);
            END

            IF OBJECT_ID(N'dbo.ImportedNormalizedRecords', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.ImportedNormalizedRecords (
                    ImportedNormalizedRecordId uniqueidentifier NOT NULL PRIMARY KEY,
                    ImportBatchId uniqueidentifier NOT NULL,
                    SourceRawRecordId uniqueidentifier NULL,
                    TransactionDate date NOT NULL,
                    PostingDate date NULL,
                    ReferenceNumber nvarchar(120) NULL,
                    Description nvarchar(500) NULL,
                    AccountCode nvarchar(100) NULL,
                    AccountName nvarchar(200) NULL,
                    DebitAmount decimal(18,2) NOT NULL,
                    CreditAmount decimal(18,2) NOT NULL,
                    NetAmount decimal(18,2) NOT NULL,
                    Currency nvarchar(3) NOT NULL,
                    CreatedAt datetime2 NOT NULL CONSTRAINT DF_ImportedNormalizedRecords_CreatedAt DEFAULT SYSUTCDATETIME(),
                    CONSTRAINT FK_ImportedNormalizedRecords_ImportBatches_ImportBatchId FOREIGN KEY (ImportBatchId) REFERENCES dbo.ImportBatches(ImportBatchId) ON DELETE CASCADE,
                    CONSTRAINT FK_ImportedNormalizedRecords_ImportedRawRecords_SourceRawRecordId FOREIGN KEY (SourceRawRecordId) REFERENCES dbo.ImportedRawRecords(ImportedRawRecordId) ON DELETE NO ACTION
                );

                CREATE INDEX IX_ImportedNormalizedRecords_ImportBatchId ON dbo.ImportedNormalizedRecords(ImportBatchId);
                CREATE INDEX IX_ImportedNormalizedRecords_TransactionDate ON dbo.ImportedNormalizedRecords(TransactionDate);
            END

            IF OBJECT_ID(N'dbo.ImportMappingTemplates', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.ImportMappingTemplates (
                    ImportMappingTemplateId uniqueidentifier NOT NULL PRIMARY KEY,
                    Name nvarchar(150) NOT NULL,
                    SourceType nvarchar(100) NOT NULL,
                    CanonicalSchemaVersion nvarchar(30) NOT NULL CONSTRAINT DF_ImportMappingTemplates_CanonicalSchemaVersion DEFAULT (N'v1'),
                    MappingJson nvarchar(max) NOT NULL,
                    Version int NOT NULL CONSTRAINT DF_ImportMappingTemplates_Version DEFAULT (1),
                    IsActive bit NOT NULL CONSTRAINT DF_ImportMappingTemplates_IsActive DEFAULT (1),
                    CreatedByUserId uniqueidentifier NULL,
                    CreatedAt datetime2 NOT NULL CONSTRAINT DF_ImportMappingTemplates_CreatedAt DEFAULT SYSUTCDATETIME(),
                    UpdatedAt datetime2 NULL
                );

                CREATE UNIQUE INDEX IX_ImportMappingTemplates_Name ON dbo.ImportMappingTemplates(Name);
                CREATE INDEX IX_ImportMappingTemplates_SourceType_IsActive ON dbo.ImportMappingTemplates(SourceType, IsActive);
            END

            INSERT INTO dbo.Permissions (PermissionId, Code, Name, Description, Module)
            SELECT NEWID(), v.Code, v.Name, v.Description, v.Module
            FROM (VALUES
                (N'ADMIN.IMPORT_ARCHITECTURE.VIEW', N'Import Architecture View', N'View import architecture foundation', N'Admin'),
                                (N'ADMIN.IMPORT_ARCHITECTURE.MANAGE', N'Import Architecture Manage', N'Manage import architecture templates and metadata', N'Admin'),
                                (N'ADMIN.AUDIT_LOGS.VIEW', N'Audit Logs View', N'View tenant audit logs', N'Admin')
            ) v(Code, Name, Description, Module)
            WHERE NOT EXISTS (SELECT 1 FROM dbo.Permissions p WHERE p.Code = v.Code);

            INSERT INTO dbo.AppComponents (ComponentId, Code, Name, RoutePath, Category, Description, IsActive)
                        SELECT NEWID(), N'IMPORT_ARCHITECTURE_MGMT', N'Import Architecture', N'/app/imports/import-architecture', N'Admin', N'Tenant import foundation and canonical schema', 1
            WHERE NOT EXISTS (SELECT 1 FROM dbo.AppComponents c WHERE c.Code = N'IMPORT_ARCHITECTURE_MGMT');

                        INSERT INTO dbo.AppComponents (ComponentId, Code, Name, RoutePath, Category, Description, IsActive)
                        SELECT NEWID(), N'AUDIT_LOGS_MGMT', N'Audit Logs', N'/app/admin/audit-logs', N'Admin', N'Tenant audit logs', 1
                        WHERE NOT EXISTS (SELECT 1 FROM dbo.AppComponents c WHERE c.Code = N'AUDIT_LOGS_MGMT');

                        UPDATE dbo.AppComponents
                        SET RoutePath = N'/app/imports/import-architecture'
                        WHERE Code = N'IMPORT_ARCHITECTURE_MGMT'
                            AND RoutePath <> N'/app/imports/import-architecture';

            INSERT INTO dbo.RolePermissions (RoleId, PermissionId)
            SELECT r.RoleId, p.PermissionId
            FROM dbo.Roles r
                        INNER JOIN dbo.Permissions p ON p.Code IN (N'ADMIN.IMPORT_ARCHITECTURE.VIEW', N'ADMIN.IMPORT_ARCHITECTURE.MANAGE', N'ADMIN.AUDIT_LOGS.VIEW')
            WHERE r.Code = N'ADMIN'
              AND NOT EXISTS (
                  SELECT 1 FROM dbo.RolePermissions rp
                  WHERE rp.RoleId = r.RoleId AND rp.PermissionId = p.PermissionId
              );
            """;

        private static string BuildTenantImportBatchMappingLinkSql() =>
            """
            IF OBJECT_ID(N'dbo.ImportBatches', N'U') IS NULL OR OBJECT_ID(N'dbo.ImportMappingTemplates', N'U') IS NULL
            BEGIN
                RETURN;
            END

            IF COL_LENGTH(N'dbo.ImportBatches', N'MappingTemplateId') IS NULL
            BEGIN
                ALTER TABLE dbo.ImportBatches
                ADD MappingTemplateId uniqueidentifier NULL;
            END

            IF NOT EXISTS (
                SELECT 1
                FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'dbo.ImportBatches')
                  AND name = N'IX_ImportBatches_MappingTemplateId')
            BEGIN
                CREATE INDEX IX_ImportBatches_MappingTemplateId ON dbo.ImportBatches(MappingTemplateId);
            END

            IF NOT EXISTS (
                SELECT 1
                FROM sys.foreign_keys
                WHERE name = N'FK_ImportBatches_ImportMappingTemplates_MappingTemplateId')
            BEGIN
                ALTER TABLE dbo.ImportBatches
                ADD CONSTRAINT FK_ImportBatches_ImportMappingTemplates_MappingTemplateId
                    FOREIGN KEY (MappingTemplateId)
                    REFERENCES dbo.ImportMappingTemplates(ImportMappingTemplateId)
                    ON DELETE SET NULL;
            END
            """;

        private static string BuildTenantImportWorkbenchPermissionsSql() =>
            """
            INSERT INTO dbo.Permissions (PermissionId, Code, Name, Description, Module)
            SELECT NEWID(), v.Code, v.Name, v.Description, v.Module
            FROM (VALUES
                (N'ADMIN.IMPORT_WORKBENCH.VIEW', N'Import Workbench View', N'View import workbench', N'Admin')
            ) v(Code, Name, Description, Module)
            WHERE NOT EXISTS (SELECT 1 FROM dbo.Permissions p WHERE p.Code = v.Code);

            INSERT INTO dbo.AppComponents (ComponentId, Code, Name, RoutePath, Category, Description, IsActive)
            SELECT NEWID(), N'IMPORT_WORKBENCH_MGMT', N'Import Workbench', N'/app/imports/workbench', N'Admin', N'Tenant imports processing workspace', 1
            WHERE NOT EXISTS (SELECT 1 FROM dbo.AppComponents c WHERE c.Code = N'IMPORT_WORKBENCH_MGMT');

            INSERT INTO dbo.RolePermissions (RoleId, PermissionId)
            SELECT r.RoleId, p.PermissionId
            FROM dbo.Roles r
            INNER JOIN dbo.Permissions p ON p.Code IN (N'ADMIN.IMPORT_WORKBENCH.VIEW')
            WHERE r.Code = N'ADMIN'
              AND NOT EXISTS (
                  SELECT 1 FROM dbo.RolePermissions rp
                  WHERE rp.RoleId = r.RoleId AND rp.PermissionId = p.PermissionId
              );
            """;

        private static string BuildTenantBankAccountsSql() =>
            """
            IF OBJECT_ID(N'dbo.BankAccounts', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.BankAccounts (
                    BankAccountId uniqueidentifier NOT NULL PRIMARY KEY,
                    BankName nvarchar(200) NOT NULL,
                    AccountName nvarchar(200) NOT NULL,
                    AccountNumber nvarchar(100) NOT NULL,
                    Currency nvarchar(10) NOT NULL,
                    IsActive bit NOT NULL CONSTRAINT DF_BankAccounts_IsActive DEFAULT (1),
                    CreatedAt datetime2 NOT NULL CONSTRAINT DF_BankAccounts_CreatedAt DEFAULT SYSUTCDATETIME(),
                    UpdatedAt datetime2 NULL
                );

                CREATE UNIQUE INDEX IX_BankAccounts_AccountNumber ON dbo.BankAccounts(AccountNumber);
            END
            """;

        private static string BuildTenantBankAccountsPermissionsSql() =>
            """
            INSERT INTO dbo.Permissions (PermissionId, Code, Name, Description, Module)
            SELECT NEWID(), v.Code, v.Name, v.Description, v.Module
            FROM (VALUES
                (N'ADMIN.BANK_ACCOUNTS.VIEW', N'Bank Accounts View', N'View tenant bank accounts', N'Admin'),
                (N'ADMIN.BANK_ACCOUNTS.MANAGE', N'Bank Accounts Manage', N'Manage tenant bank accounts', N'Admin')
            ) v(Code, Name, Description, Module)
            WHERE NOT EXISTS (SELECT 1 FROM dbo.Permissions p WHERE p.Code = v.Code);

            INSERT INTO dbo.AppComponents (ComponentId, Code, Name, RoutePath, Category, Description, IsActive)
            SELECT NEWID(), N'BANK_ACCOUNTS_MGMT', N'Bank Accounts', N'/app/admin/bank-accounts', N'Admin', N'Tenant bank account management', 1
            WHERE NOT EXISTS (SELECT 1 FROM dbo.AppComponents c WHERE c.Code = N'BANK_ACCOUNTS_MGMT');

            INSERT INTO dbo.RolePermissions (RoleId, PermissionId)
            SELECT r.RoleId, p.PermissionId
            FROM dbo.Roles r
            INNER JOIN dbo.Permissions p ON p.Code IN (N'ADMIN.BANK_ACCOUNTS.VIEW', N'ADMIN.BANK_ACCOUNTS.MANAGE')
            WHERE r.Code = N'ADMIN'
              AND NOT EXISTS (
                  SELECT 1 FROM dbo.RolePermissions rp
                  WHERE rp.RoleId = r.RoleId AND rp.PermissionId = p.PermissionId
              );
            """;

        // Tenant transaction tables are created here because operational data lives in each tenant DB.
        private static string BuildTenantTransactionsSql() =>
            """
            IF OBJECT_ID(N'dbo.Transactions', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.Transactions (
                    TransactionId uniqueidentifier NOT NULL PRIMARY KEY,
                    Amount decimal(18,2) NOT NULL,
                    TransactionDate datetime2 NOT NULL,
                    Description nvarchar(500) NOT NULL,
                    BankAccountId uniqueidentifier NULL,
                    TransactionType nvarchar(20) NOT NULL,
                    PaymentMethod nvarchar(20) NOT NULL,
                    TransactionState nvarchar(30) NOT NULL CONSTRAINT DF_Transactions_TransactionState DEFAULT (N'Pending'),
                    CreatedAt datetime2 NOT NULL CONSTRAINT DF_Transactions_CreatedAt DEFAULT SYSUTCDATETIME(),
                    UpdatedAt datetime2 NULL,
                    CONSTRAINT CK_Transactions_Amount_Positive CHECK (Amount > 0),
                    CONSTRAINT CK_Transactions_TransactionType CHECK (TransactionType IN (N'CashIn', N'CashOut')),
                    CONSTRAINT CK_Transactions_PaymentMethod CHECK (PaymentMethod IN (N'Cash', N'Card')),
                    CONSTRAINT CK_Transactions_TransactionState CHECK (TransactionState IN (N'Pending', N'Approved', N'Rejected', N'NeedsBankMatch', N'JournalReady')),
                    CONSTRAINT CK_Transactions_PaymentMethod_BankAccount CHECK (PaymentMethod <> N'Card' OR BankAccountId IS NOT NULL),
                    CONSTRAINT FK_Transactions_BankAccounts_BankAccountId FOREIGN KEY (BankAccountId) REFERENCES dbo.BankAccounts(BankAccountId) ON DELETE NO ACTION
                );

                CREATE INDEX IX_Transactions_TransactionDate ON dbo.Transactions(TransactionDate);
                CREATE INDEX IX_Transactions_BankAccountId ON dbo.Transactions(BankAccountId);
                CREATE INDEX IX_Transactions_TransactionState ON dbo.Transactions(TransactionState);
            END

            IF OBJECT_ID(N'dbo.TransactionStateHistories', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.TransactionStateHistories (
                    TransactionStateHistoryId uniqueidentifier NOT NULL PRIMARY KEY,
                    TransactionId uniqueidentifier NOT NULL,
                    FromState nvarchar(30) NOT NULL,
                    ToState nvarchar(30) NOT NULL,
                    ChangedByUserId uniqueidentifier NULL,
                    ChangedAt datetime2 NOT NULL CONSTRAINT DF_TransactionStateHistories_ChangedAt DEFAULT SYSUTCDATETIME(),
                    CONSTRAINT CK_TransactionStateHistories_FromState CHECK (FromState IN (N'Pending', N'Approved', N'Rejected', N'NeedsBankMatch', N'JournalReady')),
                    CONSTRAINT CK_TransactionStateHistories_ToState CHECK (ToState IN (N'Pending', N'Approved', N'Rejected', N'NeedsBankMatch', N'JournalReady')),
                    CONSTRAINT FK_TransactionStateHistories_Transactions_TransactionId FOREIGN KEY (TransactionId) REFERENCES dbo.Transactions(TransactionId) ON DELETE CASCADE
                );

                CREATE INDEX IX_TransactionStateHistories_TransactionId ON dbo.TransactionStateHistories(TransactionId);
                CREATE INDEX IX_TransactionStateHistories_ChangedAt ON dbo.TransactionStateHistories(ChangedAt);
            END
            """;

        private static string BuildTenantTransactionPermissionsSql() =>
            """
            INSERT INTO dbo.Permissions (PermissionId, Code, Name, Description, Module)
            SELECT NEWID(), v.Code, v.Name, v.Description, v.Module
            FROM (VALUES
                (N'ADMIN.TRANSACTIONS.VIEW', N'Transactions View', N'View tenant transactions', N'Admin'),
                (N'ADMIN.TRANSACTIONS.MANAGE', N'Transactions Manage', N'Manage tenant transactions', N'Admin')
            ) v(Code, Name, Description, Module)
            WHERE NOT EXISTS (SELECT 1 FROM dbo.Permissions p WHERE p.Code = v.Code);

            INSERT INTO dbo.AppComponents (ComponentId, Code, Name, RoutePath, Category, Description, IsActive)
            SELECT NEWID(), N'TRANSACTIONS_MGMT', N'Transactions', N'/app/admin/transactions', N'Admin', N'Tenant transaction management', 1
            WHERE NOT EXISTS (SELECT 1 FROM dbo.AppComponents c WHERE c.Code = N'TRANSACTIONS_MGMT');

            INSERT INTO dbo.RolePermissions (RoleId, PermissionId)
            SELECT r.RoleId, p.PermissionId
            FROM dbo.Roles r
            INNER JOIN dbo.Permissions p ON p.Code IN (N'ADMIN.TRANSACTIONS.VIEW', N'ADMIN.TRANSACTIONS.MANAGE')
            WHERE r.Code = N'ADMIN'
              AND NOT EXISTS (
                  SELECT 1 FROM dbo.RolePermissions rp
                  WHERE rp.RoleId = r.RoleId AND rp.PermissionId = p.PermissionId
              );
            """;

        // Adds approval metadata without rebuilding tenant transaction tables already in use.
        private static string BuildTenantTransactionApprovalFieldsSql() =>
            """
            IF OBJECT_ID(N'dbo.Transactions', N'U') IS NOT NULL
            BEGIN
                IF COL_LENGTH(N'dbo.Transactions', N'CreatedByUserId') IS NULL
                BEGIN
                    ALTER TABLE dbo.Transactions ADD CreatedByUserId uniqueidentifier NULL;
                END

                IF COL_LENGTH(N'dbo.Transactions', N'ApprovedAt') IS NULL
                BEGIN
                    ALTER TABLE dbo.Transactions ADD ApprovedAt datetime2 NULL;
                END

                IF COL_LENGTH(N'dbo.Transactions', N'ApprovedByUserId') IS NULL
                BEGIN
                    ALTER TABLE dbo.Transactions ADD ApprovedByUserId uniqueidentifier NULL;
                END

                IF COL_LENGTH(N'dbo.Transactions', N'RejectedAt') IS NULL
                BEGIN
                    ALTER TABLE dbo.Transactions ADD RejectedAt datetime2 NULL;
                END

                IF COL_LENGTH(N'dbo.Transactions', N'RejectedByUserId') IS NULL
                BEGIN
                    ALTER TABLE dbo.Transactions ADD RejectedByUserId uniqueidentifier NULL;
                END

                IF COL_LENGTH(N'dbo.Transactions', N'RejectionReason') IS NULL
                BEGIN
                    ALTER TABLE dbo.Transactions ADD RejectionReason nvarchar(500) NULL;
                END

                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE object_id = OBJECT_ID(N'dbo.Transactions')
                      AND name = N'IX_Transactions_CreatedByUserId')
                BEGIN
                    CREATE INDEX IX_Transactions_CreatedByUserId ON dbo.Transactions(CreatedByUserId);
                END

                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE object_id = OBJECT_ID(N'dbo.Transactions')
                      AND name = N'IX_Transactions_ApprovedByUserId')
                BEGIN
                    CREATE INDEX IX_Transactions_ApprovedByUserId ON dbo.Transactions(ApprovedByUserId);
                END

                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE object_id = OBJECT_ID(N'dbo.Transactions')
                      AND name = N'IX_Transactions_RejectedByUserId')
                BEGIN
                    CREATE INDEX IX_Transactions_RejectedByUserId ON dbo.Transactions(RejectedByUserId);
                END
            END

            IF OBJECT_ID(N'dbo.TransactionStateHistories', N'U') IS NOT NULL
            BEGIN
                IF COL_LENGTH(N'dbo.TransactionStateHistories', N'Note') IS NULL
                BEGIN
                    ALTER TABLE dbo.TransactionStateHistories ADD Note nvarchar(500) NULL;
                END
            END
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
