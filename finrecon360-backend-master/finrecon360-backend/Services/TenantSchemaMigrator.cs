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
        private const string MigrationImportArchitectureExtensions = "202605010001_TenantImportArchitectureExtensions";
        private const string MigrationImportBatchMappingLink = "202604100001_TenantImportBatchMappingLink";
        private const string MigrationImportWorkbenchPermissions = "202604270001_TenantImportWorkbenchPermissions";
        private const string MigrationBankAccounts = "202604230001_TenantBankAccounts";
        private const string MigrationBankAccountsPermissions = "202604230002_TenantBankAccountsPermissions";
        private const string MigrationTransactions = "202604230003_TenantTransactions";
        private const string MigrationTransactionPermissions = "202604230004_TenantTransactionPermissions";
        private const string MigrationTransactionApprovalFields = "202604230005_TenantTransactionApprovalFields";
        private const string MigrationTransactionReferenceNumber = "202605090001_TenantTransactionReferenceNumber";
        private const string MigrationImportedNormalizedRecordTimestamps = "202605040001_ImportedNormalizedRecordTimestamps";
        private const string MigrationSettlementIdColumn = "202605020001_AddSettlementIdToImportedNormalizedRecords";
        private const string MigrationReconciliationEntities = "202605020002_CreateReconciliationMatchGroupsAndEvents";
        private const string MigrationReconciliationCascadeSafety = "202605020002a_ReconciliationCascadeSafety";
        private const string MigrationReconciliationConfirmation = "202605020003_ReconciliationConfirmationAndMatchStatus";
        private const string MigrationJournalEntries = "202605020004_JournalEntriesAndReconciliationPermissions";
        private const string MigrationGranularPermissions = "202605020005_GranularImportAndReconciliationPermissions";
        private const string MigrationSourceScopedPermissions = "202605020006_SourceTypesScopedCashierPermissions";
        private const string MigrationAllSourceScopedPermissions = "202605020007_AllSourceTypesScopedPermissions";
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
            await ApplyMigrationIfMissingAsync(connection, MigrationImportArchitectureExtensions, BuildTenantImportArchitectureExtensionsSql(), cancellationToken);
            await ApplyMigrationIfMissingAsync(connection, MigrationImportBatchMappingLink, BuildTenantImportBatchMappingLinkSql(), cancellationToken);
            await ApplyMigrationIfMissingAsync(connection, MigrationImportWorkbenchPermissions, BuildTenantImportWorkbenchPermissionsSql(), cancellationToken);
            await ApplyMigrationIfMissingAsync(connection, MigrationBankAccounts, BuildTenantBankAccountsSql(), cancellationToken);
            await ApplyMigrationIfMissingAsync(connection, MigrationBankAccountsPermissions, BuildTenantBankAccountsPermissionsSql(), cancellationToken);
            await ApplyMigrationIfMissingAsync(connection, MigrationTransactions, BuildTenantTransactionsSql(), cancellationToken);
            await ApplyMigrationIfMissingAsync(connection, MigrationTransactionPermissions, BuildTenantTransactionPermissionsSql(), cancellationToken);
            await ApplyMigrationIfMissingAsync(connection, MigrationTransactionApprovalFields, BuildTenantTransactionApprovalFieldsSql(), cancellationToken);
            await ApplyMigrationIfMissingAsync(connection, MigrationTransactionReferenceNumber, BuildTenantTransactionReferenceNumberSql(), cancellationToken);
            await ApplyMigrationIfMissingAsync(connection, MigrationImportedNormalizedRecordTimestamps, BuildImportedNormalizedRecordTimestampSql(), cancellationToken);
            await ApplyMigrationIfMissingAsync(connection, MigrationSettlementIdColumn, BuildSettlementIdColumnSql(), cancellationToken);
            await ApplyMigrationIfMissingAsync(connection, MigrationReconciliationEntities, BuildReconciliationEntitiesSql(), cancellationToken);
            await ApplyMigrationIfMissingAsync(connection, MigrationReconciliationCascadeSafety, BuildReconciliationCascadeSafetySql(), cancellationToken);
            await ApplyMigrationIfMissingAsync(connection, MigrationReconciliationConfirmation, BuildReconciliationConfirmationSql(), cancellationToken);
            await ApplyMigrationIfMissingAsync(connection, MigrationJournalEntries, BuildJournalEntriesSql(), cancellationToken);
            await ApplyMigrationIfMissingAsync(connection, MigrationGranularPermissions, BuildGranularPermissionsSql(), cancellationToken);
            await ApplyMigrationIfMissingAsync(connection, MigrationSourceScopedPermissions, BuildSourceScopedPermissionsSql(), cancellationToken);
            await ApplyMigrationIfMissingAsync(connection, MigrationAllSourceScopedPermissions, BuildAllSourceScopedPermissionsSql(), cancellationToken);
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
                    TransactionDate datetime2 NOT NULL,
                    TransactionType nvarchar(30) NULL,
                    PostingDate datetime2 NULL,
                    ReferenceNumber nvarchar(120) NULL,
                    Description nvarchar(500) NULL,
                    AccountCode nvarchar(100) NULL,
                    AccountName nvarchar(200) NULL,
                    GrossAmount decimal(18,2) NULL,
                    ProcessingFee decimal(18,2) NULL,
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
                CREATE INDEX IX_ImportedNormalizedRecords_ReferenceNumber_TransactionDate ON dbo.ImportedNormalizedRecords(ReferenceNumber, TransactionDate);
            END
            ELSE
            BEGIN
                IF COL_LENGTH(N'dbo.ImportedNormalizedRecords', N'TransactionType') IS NULL
                    ALTER TABLE dbo.ImportedNormalizedRecords ADD TransactionType nvarchar(30) NULL;

                IF COL_LENGTH(N'dbo.ImportedNormalizedRecords', N'GrossAmount') IS NULL
                    ALTER TABLE dbo.ImportedNormalizedRecords ADD GrossAmount decimal(18,2) NULL;

                IF COL_LENGTH(N'dbo.ImportedNormalizedRecords', N'ProcessingFee') IS NULL
                    ALTER TABLE dbo.ImportedNormalizedRecords ADD ProcessingFee decimal(18,2) NULL;
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

        private static string BuildTenantImportArchitectureExtensionsSql() =>
            """
            IF OBJECT_ID(N'dbo.ImportedNormalizedRecords', N'U') IS NOT NULL
            BEGIN
                IF COL_LENGTH(N'dbo.ImportedNormalizedRecords', N'TransactionType') IS NULL
                    ALTER TABLE dbo.ImportedNormalizedRecords ADD TransactionType nvarchar(30) NULL;

                IF COL_LENGTH(N'dbo.ImportedNormalizedRecords', N'GrossAmount') IS NULL
                    ALTER TABLE dbo.ImportedNormalizedRecords ADD GrossAmount decimal(18,2) NULL;

                IF COL_LENGTH(N'dbo.ImportedNormalizedRecords', N'ProcessingFee') IS NULL
                    ALTER TABLE dbo.ImportedNormalizedRecords ADD ProcessingFee decimal(18,2) NULL;
            END
            """;

        private static string BuildImportedNormalizedRecordTimestampSql() =>
            """
            IF OBJECT_ID(N'dbo.ImportedNormalizedRecords', N'U') IS NOT NULL
            BEGIN
                DECLARE @transactionDateType sysname = (
                    SELECT TYPE_NAME(c.user_type_id)
                    FROM sys.columns c
                    WHERE c.object_id = OBJECT_ID(N'dbo.ImportedNormalizedRecords')
                      AND c.name = N'TransactionDate'
                );

                IF @transactionDateType = N'date'
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM sys.indexes
                        WHERE object_id = OBJECT_ID(N'dbo.ImportedNormalizedRecords')
                          AND name = N'IX_ImportedNormalizedRecords_TransactionDate')
                    BEGIN
                        DROP INDEX IX_ImportedNormalizedRecords_TransactionDate ON dbo.ImportedNormalizedRecords;
                    END

                    ALTER TABLE dbo.ImportedNormalizedRecords ALTER COLUMN TransactionDate datetime2 NOT NULL;
                END

                DECLARE @postingDateType sysname = (
                    SELECT TYPE_NAME(c.user_type_id)
                    FROM sys.columns c
                    WHERE c.object_id = OBJECT_ID(N'dbo.ImportedNormalizedRecords')
                      AND c.name = N'PostingDate'
                );

                IF @postingDateType = N'date'
                BEGIN
                    ALTER TABLE dbo.ImportedNormalizedRecords ALTER COLUMN PostingDate datetime2 NULL;
                END

                IF NOT EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE object_id = OBJECT_ID(N'dbo.ImportedNormalizedRecords')
                      AND name = N'IX_ImportedNormalizedRecords_TransactionDate')
                BEGIN
                    CREATE INDEX IX_ImportedNormalizedRecords_TransactionDate
                        ON dbo.ImportedNormalizedRecords(TransactionDate);
                END

                IF NOT EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE object_id = OBJECT_ID(N'dbo.ImportedNormalizedRecords')
                      AND name = N'IX_ImportedNormalizedRecords_ReferenceNumber_TransactionDate')
                BEGIN
                    CREATE INDEX IX_ImportedNormalizedRecords_ReferenceNumber_TransactionDate
                        ON dbo.ImportedNormalizedRecords(ReferenceNumber, TransactionDate);
                END
            END
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
                    ReferenceNumber nvarchar(100) NULL,
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
                CREATE INDEX IX_Transactions_ReferenceNumber ON dbo.Transactions(ReferenceNumber);
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

        private static string BuildTenantTransactionReferenceNumberSql() =>
            """
            IF OBJECT_ID(N'dbo.Transactions', N'U') IS NOT NULL
            BEGIN
                IF COL_LENGTH(N'dbo.Transactions', N'ReferenceNumber') IS NULL
                BEGIN
                    ALTER TABLE dbo.Transactions ADD ReferenceNumber nvarchar(100) NULL;
                END

                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE object_id = OBJECT_ID(N'dbo.Transactions')
                      AND name = N'IX_Transactions_ReferenceNumber')
                BEGIN
                    CREATE INDEX IX_Transactions_ReferenceNumber ON dbo.Transactions(ReferenceNumber);
                END
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

        private static string BuildSettlementIdColumnSql() =>
            """
            IF OBJECT_ID(N'dbo.ImportedNormalizedRecords', N'U') IS NOT NULL
            BEGIN
                IF COL_LENGTH(N'dbo.ImportedNormalizedRecords', N'SettlementId') IS NULL
                    ALTER TABLE dbo.ImportedNormalizedRecords ADD SettlementId nvarchar(120) NULL;

                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE object_id = OBJECT_ID(N'dbo.ImportedNormalizedRecords')
                      AND name = N'IX_ImportedNormalizedRecords_SettlementId')
                BEGIN
                    CREATE INDEX IX_ImportedNormalizedRecords_SettlementId ON dbo.ImportedNormalizedRecords(SettlementId);
                END
            END
            """;

        private static string BuildReconciliationEntitiesSql() =>
            """
            IF OBJECT_ID(N'dbo.ReconciliationMatchGroups', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.ReconciliationMatchGroups (
                    ReconciliationMatchGroupId uniqueidentifier NOT NULL PRIMARY KEY,
                    ImportBatchId uniqueidentifier NOT NULL,
                    MatchLevel nvarchar(20) NOT NULL,
                    SettlementKey nvarchar(250) NULL,
                    PrimaryEventId uniqueidentifier NULL,
                    MatchMetadataJson nvarchar(max) NULL,
                    CreatedAt datetime2 NOT NULL CONSTRAINT DF_ReconciliationMatchGroups_CreatedAt DEFAULT SYSUTCDATETIME(),
                    UpdatedAt datetime2 NULL,
                    CONSTRAINT FK_ReconciliationMatchGroups_ImportBatches_ImportBatchId FOREIGN KEY (ImportBatchId) REFERENCES dbo.ImportBatches(ImportBatchId) ON DELETE CASCADE
                );

                CREATE INDEX IX_ReconciliationMatchGroups_ImportBatchId ON dbo.ReconciliationMatchGroups(ImportBatchId);
                CREATE INDEX IX_ReconciliationMatchGroups_MatchLevel ON dbo.ReconciliationMatchGroups(MatchLevel);
                CREATE INDEX IX_ReconciliationMatchGroups_SettlementKey ON dbo.ReconciliationMatchGroups(SettlementKey);
            END

            IF OBJECT_ID(N'dbo.ReconciliationMatchedRecords', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.ReconciliationMatchedRecords (
                    ReconciliationMatchedRecordId uniqueidentifier NOT NULL PRIMARY KEY,
                    ReconciliationMatchGroupId uniqueidentifier NOT NULL,
                    ImportedNormalizedRecordId uniqueidentifier NOT NULL,
                    SourceType nvarchar(20) NOT NULL,
                    MatchAmount decimal(18,2) NOT NULL,
                    CreatedAt datetime2 NOT NULL CONSTRAINT DF_ReconciliationMatchedRecords_CreatedAt DEFAULT SYSUTCDATETIME(),
                    CONSTRAINT FK_ReconciliationMatchedRecords_ReconciliationMatchGroups_ReconciliationMatchGroupId FOREIGN KEY (ReconciliationMatchGroupId) REFERENCES dbo.ReconciliationMatchGroups(ReconciliationMatchGroupId) ON DELETE CASCADE,
                    CONSTRAINT FK_ReconciliationMatchedRecords_ImportedNormalizedRecords_ImportedNormalizedRecordId FOREIGN KEY (ImportedNormalizedRecordId) REFERENCES dbo.ImportedNormalizedRecords(ImportedNormalizedRecordId) ON DELETE NO ACTION
                );

                CREATE INDEX IX_ReconciliationMatchedRecords_ReconciliationMatchGroupId ON dbo.ReconciliationMatchedRecords(ReconciliationMatchGroupId);
                CREATE INDEX IX_ReconciliationMatchedRecords_ImportedNormalizedRecordId ON dbo.ReconciliationMatchedRecords(ImportedNormalizedRecordId);
                CREATE INDEX IX_ReconciliationMatchedRecords_SourceType ON dbo.ReconciliationMatchedRecords(SourceType);
            END

            IF OBJECT_ID(N'dbo.ReconciliationEvents', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.ReconciliationEvents (
                    ReconciliationEventId uniqueidentifier NOT NULL PRIMARY KEY,
                    ImportBatchId uniqueidentifier NOT NULL,
                    ImportedNormalizedRecordId uniqueidentifier NOT NULL,
                    EventType nvarchar(50) NOT NULL,
                    Stage nvarchar(20) NOT NULL,
                    SourceType nvarchar(20) NOT NULL,
                    Status nvarchar(50) NOT NULL CONSTRAINT DF_ReconciliationEvents_Status DEFAULT (N'Pending'),
                    DetailJson nvarchar(max) NULL,
                    CreatedAt datetime2 NOT NULL CONSTRAINT DF_ReconciliationEvents_CreatedAt DEFAULT SYSUTCDATETIME(),
                    ResolvedAt datetime2 NULL,
                    CONSTRAINT FK_ReconciliationEvents_ImportBatches_ImportBatchId FOREIGN KEY (ImportBatchId) REFERENCES dbo.ImportBatches(ImportBatchId) ON DELETE CASCADE,
                    CONSTRAINT FK_ReconciliationEvents_ImportedNormalizedRecords_ImportedNormalizedRecordId FOREIGN KEY (ImportedNormalizedRecordId) REFERENCES dbo.ImportedNormalizedRecords(ImportedNormalizedRecordId) ON DELETE NO ACTION
                );

                CREATE INDEX IX_ReconciliationEvents_ImportBatchId ON dbo.ReconciliationEvents(ImportBatchId);
                CREATE INDEX IX_ReconciliationEvents_ImportedNormalizedRecordId ON dbo.ReconciliationEvents(ImportedNormalizedRecordId);
                CREATE INDEX IX_ReconciliationEvents_EventType ON dbo.ReconciliationEvents(EventType);
                CREATE INDEX IX_ReconciliationEvents_Stage ON dbo.ReconciliationEvents(Stage);
                CREATE INDEX IX_ReconciliationEvents_SourceType ON dbo.ReconciliationEvents(SourceType);
                CREATE INDEX IX_ReconciliationEvents_Status ON dbo.ReconciliationEvents(Status);
            END
            """;

        private static string BuildReconciliationCascadeSafetySql() =>
            """
            IF OBJECT_ID(N'dbo.ReconciliationMatchedRecords', N'U') IS NOT NULL
            BEGIN
                IF EXISTS (
                    SELECT 1
                    FROM sys.foreign_keys
                    WHERE name = N'FK_ReconciliationMatchedRecords_ImportedNormalizedRecords_ImportedNormalizedRecordId'
                      AND delete_referential_action_desc = N'CASCADE')
                BEGIN
                    ALTER TABLE dbo.ReconciliationMatchedRecords
                        DROP CONSTRAINT FK_ReconciliationMatchedRecords_ImportedNormalizedRecords_ImportedNormalizedRecordId;

                    ALTER TABLE dbo.ReconciliationMatchedRecords
                        ADD CONSTRAINT FK_ReconciliationMatchedRecords_ImportedNormalizedRecords_ImportedNormalizedRecordId
                            FOREIGN KEY (ImportedNormalizedRecordId)
                            REFERENCES dbo.ImportedNormalizedRecords(ImportedNormalizedRecordId)
                            ON DELETE NO ACTION;
                END
            END

            IF OBJECT_ID(N'dbo.ReconciliationEvents', N'U') IS NOT NULL
            BEGIN
                IF EXISTS (
                    SELECT 1
                    FROM sys.foreign_keys
                    WHERE name = N'FK_ReconciliationEvents_ImportedNormalizedRecords_ImportedNormalizedRecordId'
                      AND delete_referential_action_desc = N'CASCADE')
                BEGIN
                    ALTER TABLE dbo.ReconciliationEvents
                        DROP CONSTRAINT FK_ReconciliationEvents_ImportedNormalizedRecords_ImportedNormalizedRecordId;

                    ALTER TABLE dbo.ReconciliationEvents
                        ADD CONSTRAINT FK_ReconciliationEvents_ImportedNormalizedRecords_ImportedNormalizedRecordId
                            FOREIGN KEY (ImportedNormalizedRecordId)
                            REFERENCES dbo.ImportedNormalizedRecords(ImportedNormalizedRecordId)
                            ON DELETE NO ACTION;
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

        // Adds the human-confirmation gate columns and denormalized MatchStatus to existing tables.
        // These are required before the Matcher UI and journal posting can function.
        private static string BuildReconciliationConfirmationSql() =>
            """
            IF OBJECT_ID(N'dbo.ReconciliationMatchGroups', N'U') IS NOT NULL
            BEGIN
                IF COL_LENGTH(N'dbo.ReconciliationMatchGroups', N'IsConfirmed') IS NULL
                    ALTER TABLE dbo.ReconciliationMatchGroups ADD IsConfirmed bit NOT NULL CONSTRAINT DF_ReconciliationMatchGroups_IsConfirmed DEFAULT (0);

                IF COL_LENGTH(N'dbo.ReconciliationMatchGroups', N'ConfirmedByUserId') IS NULL
                    ALTER TABLE dbo.ReconciliationMatchGroups ADD ConfirmedByUserId uniqueidentifier NULL;

                IF COL_LENGTH(N'dbo.ReconciliationMatchGroups', N'ConfirmedAt') IS NULL
                    ALTER TABLE dbo.ReconciliationMatchGroups ADD ConfirmedAt datetime2 NULL;

                IF COL_LENGTH(N'dbo.ReconciliationMatchGroups', N'IsJournalPosted') IS NULL
                    ALTER TABLE dbo.ReconciliationMatchGroups ADD IsJournalPosted bit NOT NULL CONSTRAINT DF_ReconciliationMatchGroups_IsJournalPosted DEFAULT (0);

                IF NOT EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE object_id = OBJECT_ID(N'dbo.ReconciliationMatchGroups')
                      AND name = N'IX_ReconciliationMatchGroups_IsConfirmed')
                BEGIN
                    CREATE INDEX IX_ReconciliationMatchGroups_IsConfirmed ON dbo.ReconciliationMatchGroups(IsConfirmed);
                END
            END

            IF OBJECT_ID(N'dbo.ImportedNormalizedRecords', N'U') IS NOT NULL
            BEGIN
                IF COL_LENGTH(N'dbo.ImportedNormalizedRecords', N'MatchStatus') IS NULL
                BEGIN
                    ALTER TABLE dbo.ImportedNormalizedRecords
                        ADD MatchStatus nvarchar(30) NOT NULL CONSTRAINT DF_ImportedNormalizedRecords_MatchStatus DEFAULT (N'PENDING');
                END

                IF NOT EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE object_id = OBJECT_ID(N'dbo.ImportedNormalizedRecords')
                      AND name = N'IX_ImportedNormalizedRecords_MatchStatus')
                BEGIN
                    CREATE INDEX IX_ImportedNormalizedRecords_MatchStatus ON dbo.ImportedNormalizedRecords(MatchStatus);
                END
            END
            """;

        // Creates the JournalEntries table and seeds reconciliation permissions + app components for RBAC.
        private static string BuildJournalEntriesSql() =>
            """
            IF OBJECT_ID(N'dbo.JournalEntries', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.JournalEntries (
                    JournalEntryId uniqueidentifier NOT NULL PRIMARY KEY,
                    TransactionId uniqueidentifier NULL,
                    ReconciliationMatchGroupId uniqueidentifier NULL,
                    EntryType nvarchar(50) NOT NULL,
                    Amount decimal(18,2) NOT NULL,
                    Currency nvarchar(3) NOT NULL CONSTRAINT DF_JournalEntries_Currency DEFAULT (N'LKR'),
                    PostedAt datetime2 NOT NULL CONSTRAINT DF_JournalEntries_PostedAt DEFAULT SYSUTCDATETIME(),
                    PostedByUserId uniqueidentifier NULL,
                    Notes nvarchar(1000) NULL,
                    CONSTRAINT FK_JournalEntries_Transactions_TransactionId FOREIGN KEY (TransactionId) REFERENCES dbo.Transactions(TransactionId) ON DELETE NO ACTION,
                    CONSTRAINT FK_JournalEntries_ReconciliationMatchGroups_ReconciliationMatchGroupId FOREIGN KEY (ReconciliationMatchGroupId) REFERENCES dbo.ReconciliationMatchGroups(ReconciliationMatchGroupId) ON DELETE NO ACTION
                );

                CREATE INDEX IX_JournalEntries_PostedAt ON dbo.JournalEntries(PostedAt);
                CREATE INDEX IX_JournalEntries_TransactionId ON dbo.JournalEntries(TransactionId);
                CREATE INDEX IX_JournalEntries_ReconciliationMatchGroupId ON dbo.JournalEntries(ReconciliationMatchGroupId);
            END

            -- Seed reconciliation permissions if not present
            INSERT INTO dbo.Permissions (PermissionId, Code, Name, Description, Module)
            SELECT NEWID(), v.Code, v.Name, v.Description, v.Module
            FROM (VALUES
                (N'ADMIN.RECONCILIATION.VIEW',   N'Reconciliation View',   N'View reconciliation queues and match groups',  N'Reconciliation'),
                (N'ADMIN.RECONCILIATION.MANAGE', N'Reconciliation Manage', N'Confirm match groups and post journal entries', N'Reconciliation'),
                (N'ADMIN.JOURNAL.VIEW',           N'Journal View',          N'View posted journal entries',                  N'Accounting'),
                (N'ADMIN.JOURNAL.MANAGE',         N'Journal Manage',        N'Post and manage journal entries',              N'Accounting')
            ) v(Code, Name, Description, Module)
            WHERE NOT EXISTS (SELECT 1 FROM dbo.Permissions p WHERE p.Code = v.Code);

            -- Register the Matcher and Journal screens as navigable App Components
            INSERT INTO dbo.AppComponents (ComponentId, Code, Name, RoutePath, Category, Description, IsActive)
            SELECT NEWID(), v.Code, v.Name, v.RoutePath, v.Category, v.Description, 1
            FROM (VALUES
                (N'MATCHER_MGMT',        N'Matcher',        N'/app/matcher',          N'Reconciliation', N'Bank match confirmation queue'),
                (N'JOURNAL_MGMT',        N'Journal',        N'/app/journal',           N'Accounting',     N'Posted journal entries'),
                (N'WAITING_QUEUE_MGMT',  N'Waiting Queue',  N'/app/matcher/waiting',   N'Reconciliation', N'Records waiting for settlement ID'),
                (N'SALES_VERIFY_MGMT',   N'Sales Verify',   N'/app/matcher/sales',     N'Reconciliation', N'Sales / ERP variance review queue')
            ) v(Code, Name, RoutePath, Category, Description)
            WHERE NOT EXISTS (SELECT 1 FROM dbo.AppComponents c WHERE c.Code = v.Code);

            -- Grant new reconciliation + journal permissions to ADMIN role
            INSERT INTO dbo.RolePermissions (RoleId, PermissionId)
            SELECT r.RoleId, p.PermissionId
            FROM dbo.Roles r
            INNER JOIN dbo.Permissions p ON p.Code IN (
                N'ADMIN.RECONCILIATION.VIEW', N'ADMIN.RECONCILIATION.MANAGE',
                N'ADMIN.JOURNAL.VIEW', N'ADMIN.JOURNAL.MANAGE'
            )
            WHERE r.Code = N'ADMIN'
              AND NOT EXISTS (
                  SELECT 1 FROM dbo.RolePermissions rp
                  WHERE rp.RoleId = r.RoleId AND rp.PermissionId = p.PermissionId
              );

            -- Grant read-only reconciliation + journal permissions to MANAGER and REVIEWER
            INSERT INTO dbo.RolePermissions (RoleId, PermissionId)
            SELECT r.RoleId, p.PermissionId
            FROM dbo.Roles r
            INNER JOIN dbo.Permissions p ON p.Code IN (
                N'ADMIN.RECONCILIATION.VIEW', N'ADMIN.JOURNAL.VIEW'
            )
            WHERE r.Code IN (N'MANAGER', N'REVIEWER')
              AND NOT EXISTS (
                  SELECT 1 FROM dbo.RolePermissions rp
                  WHERE rp.RoleId = r.RoleId AND rp.PermissionId = p.PermissionId
              );
            """;
        // Seeds granular IMPORTS, IMPORT_ARCHITECTURE, RECONCILIATION, and JOURNAL permissions.
        // WHY: Replaces the blunt VIEW/MANAGE binary split with independently grantable atomic actions.
        // The PermissionHandler AliasMap implements the MANAGE→VIEW implication so VIEW is only
        // explicitly granted to roles that need read access but have no write permission.
        private static string BuildGranularPermissionsSql() =>
            """
            -- ── Seed new granular permissions (idempotent) ───────────────────────────────────
            INSERT INTO dbo.Permissions (PermissionId, Code, Name, Description, Module)
            SELECT NEWID(), v.Code, v.Name, v.Description, v.Module
            FROM (VALUES
                -- IMPORTS module
                (N'ADMIN.IMPORTS.VIEW',    N'Import View',    N'View import batches and history',           N'Imports'),
                (N'ADMIN.IMPORTS.CREATE',  N'Import Upload',  N'Upload and start an import batch',          N'Imports'),
                (N'ADMIN.IMPORTS.EDIT',    N'Import Edit',    N'Parse, map, validate and correct rows',     N'Imports'),
                (N'ADMIN.IMPORTS.COMMIT',  N'Import Commit',  N'Commit a validated import batch',           N'Imports'),
                (N'ADMIN.IMPORTS.DELETE',  N'Import Delete',  N'Delete an import batch permanently',        N'Imports'),
                -- IMPORT_ARCHITECTURE module
                (N'ADMIN.IMPORT_ARCHITECTURE.CREATE', N'Architecture Create', N'Create mapping templates', N'Imports'),
                (N'ADMIN.IMPORT_ARCHITECTURE.EDIT',   N'Architecture Edit',   N'Update mapping templates', N'Imports'),
                (N'ADMIN.IMPORT_ARCHITECTURE.DELETE', N'Architecture Delete', N'Deactivate or delete mapping templates', N'Imports'),
                -- RECONCILIATION module
                (N'ADMIN.RECONCILIATION.CONFIRM', N'Reconciliation Confirm', N'Confirm a reconciliation match group',  N'Reconciliation'),
                (N'ADMIN.RECONCILIATION.RESOLVE', N'Reconciliation Resolve', N'Attach settlement IDs and resolve exceptions', N'Reconciliation'),
                -- JOURNAL module
                (N'ADMIN.JOURNAL.POST',    N'Journal Post',   N'Post journal entries from transactions or match groups', N'Accounting')
            ) v(Code, Name, Description, Module)
            WHERE NOT EXISTS (SELECT 1 FROM dbo.Permissions p WHERE p.Code = v.Code);

            -- ── ADMIN role: grant ALL new permissions ────────────────────────────────────────
            INSERT INTO dbo.RolePermissions (RoleId, PermissionId)
            SELECT r.RoleId, p.PermissionId
            FROM dbo.Roles r
            INNER JOIN dbo.Permissions p ON p.Code IN (
                N'ADMIN.IMPORTS.VIEW',   N'ADMIN.IMPORTS.CREATE', N'ADMIN.IMPORTS.EDIT',
                N'ADMIN.IMPORTS.COMMIT', N'ADMIN.IMPORTS.DELETE',
                N'ADMIN.IMPORT_ARCHITECTURE.CREATE', N'ADMIN.IMPORT_ARCHITECTURE.EDIT', N'ADMIN.IMPORT_ARCHITECTURE.DELETE',
                N'ADMIN.RECONCILIATION.CONFIRM', N'ADMIN.RECONCILIATION.RESOLVE',
                N'ADMIN.JOURNAL.POST'
            )
            WHERE r.Code = N'ADMIN'
              AND NOT EXISTS (
                  SELECT 1 FROM dbo.RolePermissions rp
                  WHERE rp.RoleId = r.RoleId AND rp.PermissionId = p.PermissionId
              );

            -- ── MANAGER role: upload + parse/map/validate + confirm + resolve (no commit, no delete, no import-arch write) ──
            INSERT INTO dbo.RolePermissions (RoleId, PermissionId)
            SELECT r.RoleId, p.PermissionId
            FROM dbo.Roles r
            INNER JOIN dbo.Permissions p ON p.Code IN (
                N'ADMIN.IMPORTS.VIEW',  N'ADMIN.IMPORTS.CREATE', N'ADMIN.IMPORTS.EDIT',
                N'ADMIN.IMPORT_ARCHITECTURE.VIEW',
                N'ADMIN.RECONCILIATION.CONFIRM', N'ADMIN.RECONCILIATION.RESOLVE'
            )
            WHERE r.Code = N'MANAGER'
              AND NOT EXISTS (
                  SELECT 1 FROM dbo.RolePermissions rp
                  WHERE rp.RoleId = r.RoleId AND rp.PermissionId = p.PermissionId
              );

            -- ── REVIEWER role: VIEW only across imports, reconciliation, journal ─────────────
            INSERT INTO dbo.RolePermissions (RoleId, PermissionId)
            SELECT r.RoleId, p.PermissionId
            FROM dbo.Roles r
            INNER JOIN dbo.Permissions p ON p.Code IN (
                N'ADMIN.IMPORTS.VIEW',
                N'ADMIN.IMPORT_ARCHITECTURE.VIEW',
                N'ADMIN.RECONCILIATION.VIEW',
                N'ADMIN.JOURNAL.VIEW'
            )
            WHERE r.Code = N'REVIEWER'
              AND NOT EXISTS (
                  SELECT 1 FROM dbo.RolePermissions rp
                  WHERE rp.RoleId = r.RoleId AND rp.PermissionId = p.PermissionId
              );
            """;

        // Seeds source-type-scoped POS permissions and the CASHIER role.
        // WHY: A CASHIER should be able to upload POS files, parse/validate/commit them,
        // and resolve POS reconciliation exceptions — but must not touch ERP, GATEWAY, or BANK data.
        // The SourceTypeScope helper enforces this at the API layer; this migration ensures the
        // DB has the matching permission codes and role assignment.
        private static string BuildSourceScopedPermissionsSql() =>
            """
            -- ── Seed source-type-scoped permission codes (idempotent) ───────────────────────
            INSERT INTO dbo.Permissions (PermissionId, Code, Name, Description, Module)
            SELECT NEWID(), v.Code, v.Name, v.Description, v.Module
            FROM (VALUES
                -- POS-scoped IMPORTS
                (N'ADMIN.IMPORTS.POS.CREATE', N'POS Import Upload',    N'Upload POS import files only',                N'Imports'),
                (N'ADMIN.IMPORTS.POS.EDIT',   N'POS Import Edit',      N'Parse, map and validate POS import batches',   N'Imports'),
                (N'ADMIN.IMPORTS.POS.COMMIT', N'POS Import Commit',    N'Commit validated POS import batches',          N'Imports'),
                -- POS-scoped RECONCILIATION
                (N'ADMIN.RECONCILIATION.POS.RESOLVE', N'POS Recon Resolve', N'Resolve POS reconciliation exceptions', N'Reconciliation')
            ) v(Code, Name, Description, Module)
            WHERE NOT EXISTS (SELECT 1 FROM dbo.Permissions p WHERE p.Code = v.Code);

            -- ── Grant POS-scoped permissions to ADMIN by default ───────────────────────────
            INSERT INTO dbo.RolePermissions (RoleId, PermissionId)
            SELECT r.RoleId, p.PermissionId
            FROM dbo.Roles r
            INNER JOIN dbo.Permissions p ON p.Code IN (
                N'ADMIN.IMPORTS.POS.CREATE',
                N'ADMIN.IMPORTS.POS.EDIT',
                N'ADMIN.IMPORTS.POS.COMMIT',
                N'ADMIN.RECONCILIATION.POS.RESOLVE'
            )
            WHERE r.Code = N'ADMIN'
              AND NOT EXISTS (
                  SELECT 1 FROM dbo.RolePermissions rp
                  WHERE rp.RoleId = r.RoleId AND rp.PermissionId = p.PermissionId
              );

            -- ── Create CASHIER role if it does not exist ─────────────────────────────────────
            INSERT INTO dbo.Roles (RoleId, Name, Code, Description, IsActive, CreatedAt)
            SELECT NEWID(),
                   N'Cashier',
                   N'CASHIER',
                   N'POS operations only — can import, process and verify POS data; no access to ERP, GATEWAY or BANK workflows.',
                   1,
                   GETUTCDATE()
            WHERE NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE Code = N'CASHIER');

            -- ── Grant POS-scoped permissions to CASHIER ───────────────────────────────────────
            INSERT INTO dbo.RolePermissions (RoleId, PermissionId)
            SELECT r.RoleId, p.PermissionId
            FROM dbo.Roles r
            INNER JOIN dbo.Permissions p ON p.Code IN (
                N'ADMIN.IMPORTS.POS.CREATE',
                N'ADMIN.IMPORTS.POS.EDIT',
                N'ADMIN.IMPORTS.POS.COMMIT',
                N'ADMIN.RECONCILIATION.POS.RESOLVE'
            )
            WHERE r.Code = N'CASHIER'
              AND NOT EXISTS (
                  SELECT 1 FROM dbo.RolePermissions rp
                  WHERE rp.RoleId = r.RoleId AND rp.PermissionId = p.PermissionId
              );
            """;

        // Seeds remaining source-type-scoped permissions (ERP, GATEWAY, BANK)
        private static string BuildAllSourceScopedPermissionsSql() =>
            """
            -- ── Seed source-type-scoped permission codes (idempotent) ───────────────────────
            INSERT INTO dbo.Permissions (PermissionId, Code, Name, Description, Module)
            SELECT NEWID(), v.Code, v.Name, v.Description, v.Module
            FROM (VALUES
                -- ERP-scoped IMPORTS
                (N'ADMIN.IMPORTS.ERP.CREATE', N'ERP Import Upload',    N'Upload ERP import files only',                N'Imports'),
                (N'ADMIN.IMPORTS.ERP.EDIT',   N'ERP Import Edit',      N'Parse, map and validate ERP import batches',   N'Imports'),
                (N'ADMIN.IMPORTS.ERP.COMMIT', N'ERP Import Commit',    N'Commit validated ERP import batches',          N'Imports'),
                (N'ADMIN.RECONCILIATION.ERP.RESOLVE', N'ERP Recon Resolve', N'Resolve ERP reconciliation exceptions', N'Reconciliation'),
                
                -- GATEWAY-scoped IMPORTS
                (N'ADMIN.IMPORTS.GATEWAY.CREATE', N'Gateway Import Upload',    N'Upload Gateway import files only',                N'Imports'),
                (N'ADMIN.IMPORTS.GATEWAY.EDIT',   N'Gateway Import Edit',      N'Parse, map and validate Gateway import batches',   N'Imports'),
                (N'ADMIN.IMPORTS.GATEWAY.COMMIT', N'Gateway Import Commit',    N'Commit validated Gateway import batches',          N'Imports'),
                (N'ADMIN.RECONCILIATION.GATEWAY.RESOLVE', N'Gateway Recon Resolve', N'Resolve Gateway reconciliation exceptions', N'Reconciliation'),

                -- BANK-scoped IMPORTS
                (N'ADMIN.IMPORTS.BANK.CREATE', N'Bank Import Upload',    N'Upload Bank import files only',                N'Imports'),
                (N'ADMIN.IMPORTS.BANK.EDIT',   N'Bank Import Edit',      N'Parse, map and validate Bank import batches',   N'Imports'),
                (N'ADMIN.IMPORTS.BANK.COMMIT', N'Bank Import Commit',    N'Commit validated Bank import batches',          N'Imports'),
                (N'ADMIN.RECONCILIATION.BANK.RESOLVE', N'Bank Recon Resolve', N'Resolve Bank reconciliation exceptions', N'Reconciliation')
            ) v(Code, Name, Description, Module)
            WHERE NOT EXISTS (SELECT 1 FROM dbo.Permissions p WHERE p.Code = v.Code);

            -- ── Grant remaining source-type-scoped permissions to ADMIN by default ──────────
            INSERT INTO dbo.RolePermissions (RoleId, PermissionId)
            SELECT r.RoleId, p.PermissionId
            FROM dbo.Roles r
            INNER JOIN dbo.Permissions p ON p.Code IN (
                N'ADMIN.IMPORTS.ERP.CREATE',
                N'ADMIN.IMPORTS.ERP.EDIT',
                N'ADMIN.IMPORTS.ERP.COMMIT',
                N'ADMIN.RECONCILIATION.ERP.RESOLVE',
                N'ADMIN.IMPORTS.GATEWAY.CREATE',
                N'ADMIN.IMPORTS.GATEWAY.EDIT',
                N'ADMIN.IMPORTS.GATEWAY.COMMIT',
                N'ADMIN.RECONCILIATION.GATEWAY.RESOLVE',
                N'ADMIN.IMPORTS.BANK.CREATE',
                N'ADMIN.IMPORTS.BANK.EDIT',
                N'ADMIN.IMPORTS.BANK.COMMIT',
                N'ADMIN.RECONCILIATION.BANK.RESOLVE'
            )
            WHERE r.Code = N'ADMIN'
              AND NOT EXISTS (
                  SELECT 1 FROM dbo.RolePermissions rp
                  WHERE rp.RoleId = r.RoleId AND rp.PermissionId = p.PermissionId
              );
            """;
    }
}
