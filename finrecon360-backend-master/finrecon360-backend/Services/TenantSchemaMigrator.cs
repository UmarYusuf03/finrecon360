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
        private const string MigrationTransactionCardLast4 = "202608160001_TenantTransactionCardLast4";
        private const string MigrationReconciliationRewriteColumns = "202608160002_TenantReconciliationRewriteColumns";
        private const string MigrationImportedRecordsMatchFields = "202608170001_TenantImportedNormalizedRecordsMatchFields";
        private const string MigrationReconciliationJournalSchema = "202608170002_TenantReconciliationJournalSchema";
        private const string MigrationReconciliationSettings = "202608170003_TenantReconciliationSettings";
        private const string MigrationImportBatchBankAccountLink = "202608170004_TenantImportBatchBankAccountLink";
        private const string MigrationChartOfAccountsAndVouchers = "202608170005_TenantChartOfAccountsAndVouchers";
        private const string MigrationChartOfAccountsCashInSeed = "202608170006_TenantChartOfAccountsCashInSeed";
        private const string MigrationPosSettlementIdentifierFields = "202608180001_TenantPosSettlementIdentifierFields";
        private const string MigrationBankingHolidays = "202608180002_TenantBankingHolidays";
        private const string MigrationReconciliationSettlementWindow = "202608180003_TenantReconciliationSettlementWindow";
        private const string MigrationPosClearingAccount = "202608180004_TenantPosClearingAccount";
        private const string MigrationReconciliationEventsMatchGroupFields = "202608180006_TenantReconciliationEventsMatchGroupFields";
        private const string MigrationTransactionReferenceNumber = "202608180007_TenantTransactionReferenceNumber";
        private const string MigrationTransactionCreatePermission = "202608180008_TenantTransactionCreatePermission";
        private const string MigrationSubscriptionsPermission = "202608180009_TenantSubscriptionsPermission";
        private const string MigrationCashFlowForecastPermission = "202608180010_TenantCashFlowForecastPermission";
        private const string MigrationFinancialReportsPermission = "202608180011_TenantFinancialReportsPermission";
        private const string MigrationReconciliationDailySnapshot = "202608190001_TenantReconciliationDailySnapshot";
        private const string MigrationTenantDailySnapshot = "202608190002_TenantDailySnapshot";
        private const string MigrationReportSchedules = "202608190003_TenantReportSchedules";
        private const string MigrationMatcherReconciliationViewGrant = "202608190004_TenantMatcherReconciliationViewGrant";
        private const string MigrationReconciliationEventsImportBatchIdNull = "202608190005_TenantReconciliationEventsImportBatchIdNull";
        private const string MigrationReconciliationEventsRecordFieldsNull = "202608250002_TenantReconciliationEventsRecordFieldsNull";
        private const string MigrationImportsScopedPermissions = "202608260001_TenantImportsScopedPermissions";
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
            await ApplyMigrationIfMissingAsync(connection, MigrationTransactionCardLast4, BuildTenantTransactionCardLast4Sql(), cancellationToken);
            await ApplyMigrationIfMissingAsync(connection, MigrationReconciliationRewriteColumns, BuildTenantReconciliationRewriteColumnsSql(), cancellationToken);
            await ApplyMigrationIfMissingAsync(connection, MigrationImportedRecordsMatchFields, BuildTenantImportedNormalizedRecordsMatchFieldsSql(), cancellationToken);
            await ApplyMigrationIfMissingAsync(connection, MigrationReconciliationJournalSchema, BuildTenantReconciliationJournalSchemaSql(), cancellationToken);
            await ApplyMigrationIfMissingAsync(connection, MigrationReconciliationSettings, BuildTenantReconciliationSettingsSql(), cancellationToken);
            await ApplyMigrationIfMissingAsync(connection, MigrationImportBatchBankAccountLink, BuildTenantImportBatchBankAccountLinkSql(), cancellationToken);
            await ApplyMigrationIfMissingAsync(connection, MigrationChartOfAccountsAndVouchers, BuildTenantChartOfAccountsAndVouchersSql(), cancellationToken);
            await ApplyMigrationIfMissingAsync(connection, MigrationChartOfAccountsCashInSeed, BuildTenantChartOfAccountsCashInSeedSql(), cancellationToken);
            await ApplyMigrationIfMissingAsync(connection, MigrationPosSettlementIdentifierFields, BuildTenantPosSettlementIdentifierFieldsSql(), cancellationToken);
            await ApplyMigrationIfMissingAsync(connection, MigrationBankingHolidays, BuildTenantBankingHolidaysSql(), cancellationToken);
            await ApplyMigrationIfMissingAsync(connection, MigrationReconciliationSettlementWindow, BuildTenantReconciliationSettlementWindowSql(), cancellationToken);
            await ApplyMigrationIfMissingAsync(connection, MigrationPosClearingAccount, BuildTenantPosClearingAccountSql(), cancellationToken);
            await ApplyMigrationIfMissingAsync(connection, MigrationReconciliationEventsMatchGroupFields, BuildTenantReconciliationEventsMatchGroupFieldsSql(), cancellationToken);
            await ApplyMigrationIfMissingAsync(connection, MigrationTransactionReferenceNumber, BuildTenantTransactionReferenceNumberSql(), cancellationToken);
            await ApplyMigrationIfMissingAsync(connection, MigrationTransactionCreatePermission, BuildTenantTransactionCreatePermissionSql(), cancellationToken);
            await ApplyMigrationIfMissingAsync(connection, MigrationSubscriptionsPermission, BuildTenantSubscriptionsPermissionSql(), cancellationToken);
            await ApplyMigrationIfMissingAsync(connection, MigrationCashFlowForecastPermission, BuildTenantCashFlowForecastPermissionSql(), cancellationToken);
            await ApplyMigrationIfMissingAsync(connection, MigrationFinancialReportsPermission, BuildTenantFinancialReportsPermissionSql(), cancellationToken);
            await ApplyMigrationIfMissingAsync(connection, MigrationReconciliationDailySnapshot, BuildTenantReconciliationDailySnapshotSql(), cancellationToken);
            await ApplyMigrationIfMissingAsync(connection, MigrationTenantDailySnapshot, BuildTenantDailySnapshotSql(), cancellationToken);
            await ApplyMigrationIfMissingAsync(connection, MigrationReportSchedules, BuildTenantReportSchedulesSql(), cancellationToken);
            await ApplyMigrationIfMissingAsync(connection, MigrationMatcherReconciliationViewGrant, BuildTenantMatcherReconciliationViewGrantSql(), cancellationToken);
            await ApplyMigrationIfMissingAsync(connection, MigrationReconciliationEventsImportBatchIdNull, BuildTenantReconciliationEventsImportBatchIdNullSql(), cancellationToken);
            await ApplyMigrationIfMissingAsync(connection, MigrationReconciliationEventsRecordFieldsNull, BuildTenantReconciliationEventsRecordFieldsNullSql(), cancellationToken);
            await ApplyMigrationIfMissingAsync(connection, MigrationImportsScopedPermissions, BuildTenantImportsScopedPermissionsSql(), cancellationToken);
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
                    TransactionType nvarchar(30) NULL,
                    PostingDate date NULL,
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

        // ADMIN.TRANSACTIONS.CREATE existed only as an AliasMap entry (satisfied by MANAGE) with
        // no row in dbo.Permissions, so it could never actually be assigned to a role from the
        // RBAC admin UI. This adds it to the catalog and grants it to ADMIN (who already had it
        // implicitly via MANAGE), so tenants can now build a cashier-style role that can log
        // transactions without also holding edit/approve/reject rights.
        private static string BuildTenantTransactionCreatePermissionSql() =>
            """
            INSERT INTO dbo.Permissions (PermissionId, Code, Name, Description, Module)
            SELECT NEWID(), v.Code, v.Name, v.Description, v.Module
            FROM (VALUES
                (N'ADMIN.TRANSACTIONS.CREATE', N'Transactions Create', N'Log new tenant transactions', N'Admin')
            ) v(Code, Name, Description, Module)
            WHERE NOT EXISTS (SELECT 1 FROM dbo.Permissions p WHERE p.Code = v.Code);

            INSERT INTO dbo.RolePermissions (RoleId, PermissionId)
            SELECT r.RoleId, p.PermissionId
            FROM dbo.Roles r
            INNER JOIN dbo.Permissions p ON p.Code = N'ADMIN.TRANSACTIONS.CREATE'
            WHERE r.Code = N'ADMIN'
              AND NOT EXISTS (
                  SELECT 1 FROM dbo.RolePermissions rp
                  WHERE rp.RoleId = r.RoleId AND rp.PermissionId = p.PermissionId
              );
            """;

        // ADMIN.SUBSCRIPTIONS.MANAGE gates AdminSubscriptionController (api/admin/subscription),
        // the tenant's own self-serve "view plan / upgrade / pay overdue balance" screen. It was
        // never seeded into any tenant schema, so no tenant admin could ever reach it — this closes
        // that gap the same way MigrationTransactionCreatePermission did for ADMIN.TRANSACTIONS.CREATE.
        private static string BuildTenantSubscriptionsPermissionSql() =>
            """
            INSERT INTO dbo.Permissions (PermissionId, Code, Name, Description, Module)
            SELECT NEWID(), v.Code, v.Name, v.Description, v.Module
            FROM (VALUES
                (N'ADMIN.SUBSCRIPTIONS.MANAGE', N'Subscription Manage', N'View and change the tenant''s own subscription plan', N'Admin')
            ) v(Code, Name, Description, Module)
            WHERE NOT EXISTS (SELECT 1 FROM dbo.Permissions p WHERE p.Code = v.Code);

            INSERT INTO dbo.RolePermissions (RoleId, PermissionId)
            SELECT r.RoleId, p.PermissionId
            FROM dbo.Roles r
            INNER JOIN dbo.Permissions p ON p.Code = N'ADMIN.SUBSCRIPTIONS.MANAGE'
            WHERE r.Code = N'ADMIN'
              AND NOT EXISTS (
                  SELECT 1 FROM dbo.RolePermissions rp
                  WHERE rp.RoleId = r.RoleId AND rp.PermissionId = p.PermissionId
              );
            """;

        // Gates the cash-flow forecasting page (api/admin/cash-flow-forecast).
        private static string BuildTenantCashFlowForecastPermissionSql() =>
            """
            INSERT INTO dbo.Permissions (PermissionId, Code, Name, Description, Module)
            SELECT NEWID(), v.Code, v.Name, v.Description, v.Module
            FROM (VALUES
                (N'ADMIN.CASH_FLOW_FORECAST.VIEW', N'Cash Flow Forecast View', N'View projected cash flow', N'Admin')
            ) v(Code, Name, Description, Module)
            WHERE NOT EXISTS (SELECT 1 FROM dbo.Permissions p WHERE p.Code = v.Code);

            INSERT INTO dbo.RolePermissions (RoleId, PermissionId)
            SELECT r.RoleId, p.PermissionId
            FROM dbo.Roles r
            INNER JOIN dbo.Permissions p ON p.Code = N'ADMIN.CASH_FLOW_FORECAST.VIEW'
            WHERE r.Code = N'ADMIN'
              AND NOT EXISTS (
                  SELECT 1 FROM dbo.RolePermissions rp
                  WHERE rp.RoleId = r.RoleId AND rp.PermissionId = p.PermissionId
              );
            """;

        // Gates the financial reports pages (api/admin/financial-reports/*): General Ledger,
        // Trial Balance, Income Statement, Balance Sheet.
        private static string BuildTenantFinancialReportsPermissionSql() =>
            """
            INSERT INTO dbo.Permissions (PermissionId, Code, Name, Description, Module)
            SELECT NEWID(), v.Code, v.Name, v.Description, v.Module
            FROM (VALUES
                (N'ADMIN.FINANCIAL_REPORTS.VIEW', N'Financial Reports View', N'View General Ledger, Trial Balance, Income Statement, and Balance Sheet reports', N'Admin')
            ) v(Code, Name, Description, Module)
            WHERE NOT EXISTS (SELECT 1 FROM dbo.Permissions p WHERE p.Code = v.Code);

            INSERT INTO dbo.RolePermissions (RoleId, PermissionId)
            SELECT r.RoleId, p.PermissionId
            FROM dbo.Roles r
            INNER JOIN dbo.Permissions p ON p.Code = N'ADMIN.FINANCIAL_REPORTS.VIEW'
            WHERE r.Code = N'ADMIN'
              AND NOT EXISTS (
                  SELECT 1 FROM dbo.RolePermissions rp
                  WHERE rp.RoleId = r.RoleId AND rp.PermissionId = p.PermissionId
              );
            """;

        // One precomputed rollup row per (SnapshotDate, MatchLevel), populated once daily by
        // ReconciliationSnapshotHostedService. See Models/ReconciliationDailySnapshot.cs for the
        // column-by-column rationale.
        private static string BuildTenantReconciliationDailySnapshotSql() =>
            """
            IF OBJECT_ID(N'dbo.ReconciliationDailySnapshots', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.ReconciliationDailySnapshots (
                    ReconciliationDailySnapshotId uniqueidentifier NOT NULL PRIMARY KEY,
                    SnapshotDate date NOT NULL,
                    MatchLevel nvarchar(20) NOT NULL,
                    MatchedCount int NOT NULL CONSTRAINT DF_ReconciliationDailySnapshots_MatchedCount DEFAULT (0),
                    ConfirmedCount int NOT NULL CONSTRAINT DF_ReconciliationDailySnapshots_ConfirmedCount DEFAULT (0),
                    ExceptionCount int NOT NULL CONSTRAINT DF_ReconciliationDailySnapshots_ExceptionCount DEFAULT (0),
                    UnmatchedCount int NOT NULL CONSTRAINT DF_ReconciliationDailySnapshots_UnmatchedCount DEFAULT (0),
                    AverageTimeToMatchHours decimal(10,2) NULL,
                    CreatedAt datetime2 NOT NULL CONSTRAINT DF_ReconciliationDailySnapshots_CreatedAt DEFAULT SYSUTCDATETIME()
                );

                CREATE UNIQUE INDEX IX_ReconciliationDailySnapshots_Date_Level ON dbo.ReconciliationDailySnapshots(SnapshotDate, MatchLevel);
            END
            """;

        // Tenant-wide counterpart to ReconciliationDailySnapshots — one row per day, covering the
        // Section 17 outputs that aren't naturally per-MatchLevel (approval backlog, journal
        // posting summary, bank reconciliation progress). See Models/TenantDailySnapshot.cs.
        private static string BuildTenantDailySnapshotSql() =>
            """
            IF OBJECT_ID(N'dbo.TenantDailySnapshots', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.TenantDailySnapshots (
                    TenantDailySnapshotId uniqueidentifier NOT NULL PRIMARY KEY,
                    SnapshotDate date NOT NULL,
                    PendingApprovalCount int NOT NULL CONSTRAINT DF_TenantDailySnapshots_PendingApprovalCount DEFAULT (0),
                    OldestPendingApprovalAgeHours decimal(10,2) NULL,
                    JournalEntriesPostedCount int NOT NULL CONSTRAINT DF_TenantDailySnapshots_JournalEntriesPostedCount DEFAULT (0),
                    JournalDebitAmountPosted decimal(18,2) NOT NULL CONSTRAINT DF_TenantDailySnapshots_JournalDebitAmountPosted DEFAULT (0),
                    BankRecordsTotalCount int NOT NULL CONSTRAINT DF_TenantDailySnapshots_BankRecordsTotalCount DEFAULT (0),
                    BankRecordsMatchedCount int NOT NULL CONSTRAINT DF_TenantDailySnapshots_BankRecordsMatchedCount DEFAULT (0),
                    CreatedAt datetime2 NOT NULL CONSTRAINT DF_TenantDailySnapshots_CreatedAt DEFAULT SYSUTCDATETIME()
                );

                CREATE UNIQUE INDEX IX_TenantDailySnapshots_Date ON dbo.TenantDailySnapshots(SnapshotDate);
            END
            """;

        // Backs Phase 5's "email me this report every Monday" scheduling feature, plus the
        // ADMIN.REPORT_SCHEDULES.MANAGE permission that gates managing them.
        private static string BuildTenantReportSchedulesSql() =>
            """
            IF OBJECT_ID(N'dbo.ReportSchedules', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.ReportSchedules (
                    ReportScheduleId uniqueidentifier NOT NULL PRIMARY KEY,
                    ReportType nvarchar(30) NOT NULL,
                    Format nvarchar(10) NOT NULL CONSTRAINT DF_ReportSchedules_Format DEFAULT (N'csv'),
                    DayOfWeek int NOT NULL,
                    RecipientEmail nvarchar(256) NOT NULL,
                    IsActive bit NOT NULL CONSTRAINT DF_ReportSchedules_IsActive DEFAULT (1),
                    CreatedByUserId uniqueidentifier NOT NULL,
                    CreatedAt datetime2 NOT NULL CONSTRAINT DF_ReportSchedules_CreatedAt DEFAULT SYSUTCDATETIME(),
                    LastRunAt datetime2 NULL,
                    NextRunAt datetime2 NOT NULL
                );

                CREATE INDEX IX_ReportSchedules_NextRunAt ON dbo.ReportSchedules(NextRunAt);
            END

            INSERT INTO dbo.Permissions (PermissionId, Code, Name, Description, Module)
            SELECT NEWID(), v.Code, v.Name, v.Description, v.Module
            FROM (VALUES
                (N'ADMIN.REPORT_SCHEDULES.MANAGE', N'Report Schedules Manage', N'Create and manage scheduled report emails', N'Admin')
            ) v(Code, Name, Description, Module)
            WHERE NOT EXISTS (SELECT 1 FROM dbo.Permissions p WHERE p.Code = v.Code);

            INSERT INTO dbo.RolePermissions (RoleId, PermissionId)
            SELECT r.RoleId, p.PermissionId
            FROM dbo.Roles r
            INNER JOIN dbo.Permissions p ON p.Code = N'ADMIN.REPORT_SCHEDULES.MANAGE'
            WHERE r.Code = N'ADMIN'
              AND NOT EXISTS (
                  SELECT 1 FROM dbo.RolePermissions rp
                  WHERE rp.RoleId = r.RoleId AND rp.PermissionId = p.PermissionId
              );
            """;

        // The Matcher nav tab is gated by MATCHER.VIEW (granted to ADMIN/MANAGER/REVIEWER/USER in
        // BuildTenantRbacSql), but the /app/matcher route it links to is guarded by
        // ADMIN.RECONCILIATION.VIEW, which BuildTenantReconciliationJournalSchemaSql only granted
        // to ADMIN. Non-ADMIN roles could see the tab but bounced to /app/not-authorized on click.
        // This is a standalone migration rather than an edit to that method's SQL because
        // MigrationReconciliationJournalSchema is tracked by name and only runs once per tenant —
        // see BuildTenantReconciliationEventsMatchGroupFieldsSql above for the same gap class.
        private static string BuildTenantMatcherReconciliationViewGrantSql() =>
            """
            INSERT INTO dbo.RolePermissions (RoleId, PermissionId)
            SELECT r.RoleId, p.PermissionId
            FROM dbo.Roles r
            INNER JOIN dbo.Permissions p ON p.Code = N'ADMIN.RECONCILIATION.VIEW'
            WHERE r.Code IN (N'MANAGER', N'REVIEWER', N'USER')
              AND NOT EXISTS (
                  SELECT 1 FROM dbo.RolePermissions rp
                  WHERE rp.RoleId = r.RoleId AND rp.PermissionId = p.PermissionId
              );
            """;

        // Adds approval metadata without rebuilding tenant transaction tables already in use.
        /// <summary>
        /// Adds the columns introduced by the reconciliation rewrite.
        ///
        /// WHY these are grouped: the rewrite added five properties across three entities, none of
        /// which had a corresponding tenant migration. The reconciliation workers query these
        /// tables on a timer, so every cycle failed with "Invalid column name" and no matching
        /// could run at all — the feature was unreachable rather than merely incomplete.
        ///
        /// Each column is added independently so a database that already has some of them (for
        /// instance one repaired by hand) upgrades cleanly rather than failing on the first
        /// duplicate.
        /// </summary>
        private static string BuildTenantReconciliationRewriteColumnsSql() =>
            """
            IF OBJECT_ID(N'dbo.ImportedNormalizedRecords', N'U') IS NOT NULL
            BEGIN
                -- Denormalised join key between a gateway payout and its bank deposit line.
                IF COL_LENGTH(N'dbo.ImportedNormalizedRecords', N'SettlementKey') IS NULL
                BEGIN
                    ALTER TABLE dbo.ImportedNormalizedRecords ADD SettlementKey nvarchar(256) NULL;
                END
            END

            IF OBJECT_ID(N'dbo.ReconciliationMatchGroups', N'U') IS NOT NULL
            BEGIN
                IF COL_LENGTH(N'dbo.ReconciliationMatchGroups', N'MatchedAmount') IS NULL
                BEGIN
                    ALTER TABLE dbo.ReconciliationMatchGroups ADD MatchedAmount decimal(18,2) NOT NULL
                        CONSTRAINT DF_ReconciliationMatchGroups_MatchedAmount DEFAULT (0);
                END

                IF COL_LENGTH(N'dbo.ReconciliationMatchGroups', N'Variance') IS NULL
                BEGIN
                    ALTER TABLE dbo.ReconciliationMatchGroups ADD Variance decimal(18,2) NOT NULL
                        CONSTRAINT DF_ReconciliationMatchGroups_Variance DEFAULT (0);
                END

                IF COL_LENGTH(N'dbo.ReconciliationMatchGroups', N'Status') IS NULL
                BEGIN
                    ALTER TABLE dbo.ReconciliationMatchGroups ADD Status nvarchar(40) NOT NULL
                        CONSTRAINT DF_ReconciliationMatchGroups_Status DEFAULT ('Pending');
                END
            END

            IF OBJECT_ID(N'dbo.ReconciliationMatchedRecords', N'U') IS NOT NULL
            BEGIN
                IF COL_LENGTH(N'dbo.ReconciliationMatchedRecords', N'LinkedAt') IS NULL
                BEGIN
                    ALTER TABLE dbo.ReconciliationMatchedRecords ADD LinkedAt datetime2 NOT NULL
                        CONSTRAINT DF_ReconciliationMatchedRecords_LinkedAt DEFAULT (SYSUTCDATETIME());
                END
            END
            """;

        /// <summary>
        /// Adds Transactions.CardLast4.
        ///
        /// WHY this was needed: the property was added to the Transaction model without a matching
        /// tenant migration, so EF generated INSERTs naming a column no tenant database had.
        /// Every transaction creation failed with "Invalid column name 'CardLast4'", and the
        /// journal posting worker failed on the same query. The control-plane migrations do not
        /// cover tenant databases — those are provisioned and upgraded here.
        /// </summary>
        private static string BuildTenantTransactionCardLast4Sql() =>
            """
            IF OBJECT_ID(N'dbo.Transactions', N'U') IS NOT NULL
            BEGIN
                IF COL_LENGTH(N'dbo.Transactions', N'CardLast4') IS NULL
                BEGIN
                    ALTER TABLE dbo.Transactions ADD CardLast4 nvarchar(4) NULL;
                END
            END
            """;

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

        // Backfills columns the reconciliation workers depend on (MatchStatus, SettlementKey)
        // that were never added to the original ImportedNormalizedRecords table definition.
        // See repair_missing_import_columns.sql — that manual script patched ReferenceNumber/
        // SettlementId on one failing tenant, but MatchStatus/SettlementKey were still missing
        // from every tenant DB, including ones already patched.
        private static string BuildTenantImportedNormalizedRecordsMatchFieldsSql() =>
            """
            IF OBJECT_ID(N'dbo.ImportedNormalizedRecords', N'U') IS NOT NULL
            BEGIN
                IF COL_LENGTH(N'dbo.ImportedNormalizedRecords', N'MatchStatus') IS NULL
                BEGIN
                    ALTER TABLE dbo.ImportedNormalizedRecords
                    ADD MatchStatus nvarchar(30) NOT NULL CONSTRAINT DF_ImportedNormalizedRecords_MatchStatus DEFAULT (N'PENDING');
                END

                IF COL_LENGTH(N'dbo.ImportedNormalizedRecords', N'SettlementId') IS NULL
                BEGIN
                    ALTER TABLE dbo.ImportedNormalizedRecords ADD SettlementId nvarchar(max) NULL;
                END

                IF COL_LENGTH(N'dbo.ImportedNormalizedRecords', N'SettlementKey') IS NULL
                BEGIN
                    ALTER TABLE dbo.ImportedNormalizedRecords ADD SettlementKey nvarchar(200) NULL;
                END

                IF NOT EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE object_id = OBJECT_ID(N'dbo.ImportedNormalizedRecords') AND name = N'IX_ImportedNormalizedRecords_MatchStatus')
                BEGIN
                    CREATE INDEX IX_ImportedNormalizedRecords_MatchStatus ON dbo.ImportedNormalizedRecords(MatchStatus);
                END

                IF NOT EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE object_id = OBJECT_ID(N'dbo.ImportedNormalizedRecords') AND name = N'IX_ImportedNormalizedRecords_SettlementKey')
                BEGIN
                    CREATE INDEX IX_ImportedNormalizedRecords_SettlementKey ON dbo.ImportedNormalizedRecords(SettlementKey);
                END

                IF NOT EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE object_id = OBJECT_ID(N'dbo.ImportedNormalizedRecords') AND name = N'IX_ImportedNormalizedRecords_ReferenceNumber_TransactionDate')
                BEGIN
                    CREATE INDEX IX_ImportedNormalizedRecords_ReferenceNumber_TransactionDate ON dbo.ImportedNormalizedRecords(ReferenceNumber, TransactionDate);
                END
            END
            """;

        // Creates the reconciliation match-group/event/journal tables. These DbSets have existed
        // on TenantDbContext for a while but were never actually created in tenant SQL Server
        // databases — worker unit tests only passed because they run against EF's InMemory
        // provider, which fabricates schema from the model regardless of what's really deployed.
        private static string BuildTenantReconciliationJournalSchemaSql() =>
            """
            IF OBJECT_ID(N'dbo.ReconciliationMatchGroups', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.ReconciliationMatchGroups (
                    ReconciliationMatchGroupId uniqueidentifier NOT NULL PRIMARY KEY,
                    MatchLevel nvarchar(20) NOT NULL,
                    SettlementKey nvarchar(200) NOT NULL,
                    IsConfirmed bit NOT NULL CONSTRAINT DF_ReconciliationMatchGroups_IsConfirmed DEFAULT (0),
                    ConfirmedByUserId uniqueidentifier NULL,
                    ConfirmedAt datetime2 NULL,
                    MatchedAmount decimal(18,2) NOT NULL,
                    Variance decimal(18,2) NOT NULL CONSTRAINT DF_ReconciliationMatchGroups_Variance DEFAULT (0),
                    Status nvarchar(30) NOT NULL CONSTRAINT DF_ReconciliationMatchGroups_Status DEFAULT (N'Pending'),
                    ImportBatchId uniqueidentifier NULL,
                    IsJournalPosted bit NOT NULL CONSTRAINT DF_ReconciliationMatchGroups_IsJournalPosted DEFAULT (0),
                    UpdatedAt datetime2 NULL,
                    PrimaryEventId uniqueidentifier NULL,
                    MatchMetadataJson nvarchar(max) NULL,
                    CreatedAt datetime2 NOT NULL CONSTRAINT DF_ReconciliationMatchGroups_CreatedAt DEFAULT SYSUTCDATETIME(),
                    CONSTRAINT FK_ReconciliationMatchGroups_ImportBatches_ImportBatchId FOREIGN KEY (ImportBatchId) REFERENCES dbo.ImportBatches(ImportBatchId) ON DELETE SET NULL
                );

                CREATE INDEX IX_ReconciliationMatchGroups_MatchLevel ON dbo.ReconciliationMatchGroups(MatchLevel);
                CREATE INDEX IX_ReconciliationMatchGroups_SettlementKey ON dbo.ReconciliationMatchGroups(SettlementKey);
                CREATE INDEX IX_ReconciliationMatchGroups_Status ON dbo.ReconciliationMatchGroups(Status);
                CREATE INDEX IX_ReconciliationMatchGroups_ImportBatchId ON dbo.ReconciliationMatchGroups(ImportBatchId);
            END

            IF OBJECT_ID(N'dbo.ReconciliationMatchedRecords', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.ReconciliationMatchedRecords (
                    ReconciliationMatchedRecordId uniqueidentifier NOT NULL PRIMARY KEY,
                    ReconciliationMatchGroupId uniqueidentifier NOT NULL,
                    ImportedNormalizedRecordId uniqueidentifier NOT NULL,
                    SourceType nvarchar(100) NOT NULL,
                    MatchAmount decimal(18,2) NOT NULL CONSTRAINT DF_ReconciliationMatchedRecords_MatchAmount DEFAULT (0),
                    LinkedAt datetime2 NOT NULL CONSTRAINT DF_ReconciliationMatchedRecords_LinkedAt DEFAULT SYSUTCDATETIME(),
                    CONSTRAINT FK_ReconciliationMatchedRecords_MatchGroups_GroupId FOREIGN KEY (ReconciliationMatchGroupId) REFERENCES dbo.ReconciliationMatchGroups(ReconciliationMatchGroupId) ON DELETE CASCADE,
                    CONSTRAINT FK_ReconciliationMatchedRecords_NormalizedRecords_RecordId FOREIGN KEY (ImportedNormalizedRecordId) REFERENCES dbo.ImportedNormalizedRecords(ImportedNormalizedRecordId) ON DELETE NO ACTION
                );

                CREATE INDEX IX_ReconciliationMatchedRecords_GroupId ON dbo.ReconciliationMatchedRecords(ReconciliationMatchGroupId);
                CREATE INDEX IX_ReconciliationMatchedRecords_RecordId ON dbo.ReconciliationMatchedRecords(ImportedNormalizedRecordId);
            END

            IF OBJECT_ID(N'dbo.ReconciliationEvents', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.ReconciliationEvents (
                    ReconciliationEventId uniqueidentifier NOT NULL PRIMARY KEY,
                    ReconciliationMatchGroupId uniqueidentifier NULL,
                    ImportedNormalizedRecordId uniqueidentifier NULL,
                    EventType nvarchar(50) NOT NULL,
                    MatchLevel nvarchar(20) NOT NULL,
                    Details nvarchar(2000) NULL,
                    Stage nvarchar(50) NULL,
                    SourceType nvarchar(100) NULL,
                    Status nvarchar(30) NULL,
                    DetailJson nvarchar(max) NULL,
                    ImportBatchId uniqueidentifier NULL,
                    CreatedAt datetime2 NOT NULL CONSTRAINT DF_ReconciliationEvents_CreatedAt DEFAULT SYSUTCDATETIME(),
                    ResolvedAt datetime2 NULL,
                    CONSTRAINT FK_ReconciliationEvents_MatchGroups_GroupId FOREIGN KEY (ReconciliationMatchGroupId) REFERENCES dbo.ReconciliationMatchGroups(ReconciliationMatchGroupId) ON DELETE NO ACTION,
                    CONSTRAINT FK_ReconciliationEvents_NormalizedRecords_RecordId FOREIGN KEY (ImportedNormalizedRecordId) REFERENCES dbo.ImportedNormalizedRecords(ImportedNormalizedRecordId) ON DELETE NO ACTION,
                    CONSTRAINT FK_ReconciliationEvents_ImportBatches_ImportBatchId FOREIGN KEY (ImportBatchId) REFERENCES dbo.ImportBatches(ImportBatchId) ON DELETE NO ACTION
                );

                CREATE INDEX IX_ReconciliationEvents_EventType ON dbo.ReconciliationEvents(EventType);
                CREATE INDEX IX_ReconciliationEvents_MatchLevel ON dbo.ReconciliationEvents(MatchLevel);
                CREATE INDEX IX_ReconciliationEvents_CreatedAt ON dbo.ReconciliationEvents(CreatedAt);
                CREATE INDEX IX_ReconciliationEvents_GroupId ON dbo.ReconciliationEvents(ReconciliationMatchGroupId);
            END

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
                    Notes nvarchar(500) NULL,
                    CONSTRAINT FK_JournalEntries_Transactions_TransactionId FOREIGN KEY (TransactionId) REFERENCES dbo.Transactions(TransactionId) ON DELETE NO ACTION,
                    CONSTRAINT FK_JournalEntries_MatchGroups_GroupId FOREIGN KEY (ReconciliationMatchGroupId) REFERENCES dbo.ReconciliationMatchGroups(ReconciliationMatchGroupId) ON DELETE NO ACTION
                );

                CREATE INDEX IX_JournalEntries_TransactionId ON dbo.JournalEntries(TransactionId);
                CREATE INDEX IX_JournalEntries_GroupId ON dbo.JournalEntries(ReconciliationMatchGroupId);
                CREATE INDEX IX_JournalEntries_PostedAt ON dbo.JournalEntries(PostedAt);
            END

            INSERT INTO dbo.Permissions (PermissionId, Code, Name, Description, Module)
            SELECT NEWID(), v.Code, v.Name, v.Description, v.Module
            FROM (VALUES
                (N'ADMIN.RECONCILIATION.VIEW', N'Reconciliation View', N'View reconciliation match groups and events', N'Reconciliation'),
                (N'ADMIN.RECONCILIATION.CONFIRM', N'Reconciliation Confirm', N'Confirm pending reconciliation matches', N'Reconciliation'),
                (N'ADMIN.RECONCILIATION.RESOLVE', N'Reconciliation Resolve', N'Resolve reconciliation exceptions', N'Reconciliation'),
                (N'ADMIN.RECONCILIATION.MANAGE', N'Reconciliation Manage', N'Manage reconciliation tolerance settings', N'Reconciliation'),
                (N'ADMIN.JOURNAL.VIEW', N'Journal View', N'View posted journal entries', N'Accounting'),
                (N'ADMIN.JOURNAL.POST', N'Journal Post', N'Post journal entries', N'Accounting')
            ) v(Code, Name, Description, Module)
            WHERE NOT EXISTS (SELECT 1 FROM dbo.Permissions p WHERE p.Code = v.Code);

            INSERT INTO dbo.RolePermissions (RoleId, PermissionId)
            SELECT r.RoleId, p.PermissionId
            FROM dbo.Roles r
            INNER JOIN dbo.Permissions p ON p.Code IN (
                N'ADMIN.RECONCILIATION.VIEW', N'ADMIN.RECONCILIATION.CONFIRM', N'ADMIN.RECONCILIATION.RESOLVE',
                N'ADMIN.RECONCILIATION.MANAGE', N'ADMIN.JOURNAL.VIEW', N'ADMIN.JOURNAL.POST')
            WHERE r.Code = N'ADMIN'
              AND NOT EXISTS (
                  SELECT 1 FROM dbo.RolePermissions rp
                  WHERE rp.RoleId = r.RoleId AND rp.PermissionId = p.PermissionId
              );
            """;

        // Single-row-per-tenant tolerance settings, replacing the seven independently
        // hardcoded `AmountTolerance = 0.01m` consts previously duplicated across the
        // matching workers.
        private static string BuildTenantReconciliationSettingsSql() =>
            """
            IF OBJECT_ID(N'dbo.ReconciliationSettings', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.ReconciliationSettings (
                    ReconciliationSettingsId uniqueidentifier NOT NULL PRIMARY KEY,
                    AmountTolerance decimal(18,4) NOT NULL CONSTRAINT DF_ReconciliationSettings_AmountTolerance DEFAULT (0.01),
                    DateToleranceDays int NOT NULL CONSTRAINT DF_ReconciliationSettings_DateToleranceDays DEFAULT (1),
                    CreatedAt datetime2 NOT NULL CONSTRAINT DF_ReconciliationSettings_CreatedAt DEFAULT SYSUTCDATETIME(),
                    UpdatedAt datetime2 NULL
                );
            END

            IF NOT EXISTS (SELECT 1 FROM dbo.ReconciliationSettings)
            BEGIN
                INSERT INTO dbo.ReconciliationSettings (ReconciliationSettingsId, AmountTolerance, DateToleranceDays)
                VALUES (NEWID(), 0.01, 1);
            END
            """;

        // Links an ImportBatch to the specific BankAccount a BANK-source file was uploaded
        // for, so reconciliation matching can be scoped per account instead of pooling every
        // BANK record tenant-wide regardless of which account it belongs to.
        private static string BuildTenantImportBatchBankAccountLinkSql() =>
            """
            IF OBJECT_ID(N'dbo.ImportBatches', N'U') IS NULL OR OBJECT_ID(N'dbo.BankAccounts', N'U') IS NULL
            BEGIN
                RETURN;
            END

            IF COL_LENGTH(N'dbo.ImportBatches', N'BankAccountId') IS NULL
            BEGIN
                ALTER TABLE dbo.ImportBatches ADD BankAccountId uniqueidentifier NULL;
            END

            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'dbo.ImportBatches') AND name = N'IX_ImportBatches_BankAccountId')
            BEGIN
                CREATE INDEX IX_ImportBatches_BankAccountId ON dbo.ImportBatches(BankAccountId);
            END

            IF NOT EXISTS (
                SELECT 1 FROM sys.foreign_keys
                WHERE name = N'FK_ImportBatches_BankAccounts_BankAccountId')
            BEGIN
                ALTER TABLE dbo.ImportBatches
                ADD CONSTRAINT FK_ImportBatches_BankAccounts_BankAccountId
                    FOREIGN KEY (BankAccountId)
                    REFERENCES dbo.BankAccounts(BankAccountId)
                    ON DELETE SET NULL;
            END
            """;

        // Minimal chart of accounts + journal voucher grouping so "posting to the journal"
        // resolves to real GL accounts and a balanced set of entries, instead of flat
        // JournalEntry rows tagged only with a free-text EntryType string.
        private static string BuildTenantChartOfAccountsAndVouchersSql() =>
            """
            IF OBJECT_ID(N'dbo.ChartOfAccounts', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.ChartOfAccounts (
                    ChartOfAccountId uniqueidentifier NOT NULL PRIMARY KEY,
                    Code nvarchar(30) NOT NULL,
                    Name nvarchar(150) NOT NULL,
                    AccountType nvarchar(20) NOT NULL,
                    IsActive bit NOT NULL CONSTRAINT DF_ChartOfAccounts_IsActive DEFAULT (1),
                    CreatedAt datetime2 NOT NULL CONSTRAINT DF_ChartOfAccounts_CreatedAt DEFAULT SYSUTCDATETIME()
                );

                CREATE UNIQUE INDEX IX_ChartOfAccounts_Code ON dbo.ChartOfAccounts(Code);
            END

            IF OBJECT_ID(N'dbo.JournalVouchers', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.JournalVouchers (
                    JournalVoucherId uniqueidentifier NOT NULL PRIMARY KEY,
                    TransactionId uniqueidentifier NULL,
                    ReconciliationMatchGroupId uniqueidentifier NULL,
                    Status nvarchar(20) NOT NULL CONSTRAINT DF_JournalVouchers_Status DEFAULT (N'Posted'),
                    PostedAt datetime2 NOT NULL CONSTRAINT DF_JournalVouchers_PostedAt DEFAULT SYSUTCDATETIME(),
                    PostedByUserId uniqueidentifier NULL,
                    CONSTRAINT FK_JournalVouchers_Transactions_TransactionId FOREIGN KEY (TransactionId) REFERENCES dbo.Transactions(TransactionId) ON DELETE NO ACTION,
                    CONSTRAINT FK_JournalVouchers_MatchGroups_GroupId FOREIGN KEY (ReconciliationMatchGroupId) REFERENCES dbo.ReconciliationMatchGroups(ReconciliationMatchGroupId) ON DELETE NO ACTION
                );

                CREATE INDEX IX_JournalVouchers_TransactionId ON dbo.JournalVouchers(TransactionId);
                CREATE INDEX IX_JournalVouchers_GroupId ON dbo.JournalVouchers(ReconciliationMatchGroupId);
            END

            IF OBJECT_ID(N'dbo.JournalEntries', N'U') IS NOT NULL
            BEGIN
                IF COL_LENGTH(N'dbo.JournalEntries', N'JournalVoucherId') IS NULL
                BEGIN
                    ALTER TABLE dbo.JournalEntries ADD JournalVoucherId uniqueidentifier NULL;
                END

                IF COL_LENGTH(N'dbo.JournalEntries', N'ChartOfAccountId') IS NULL
                BEGIN
                    ALTER TABLE dbo.JournalEntries ADD ChartOfAccountId uniqueidentifier NULL;
                END

                IF NOT EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE object_id = OBJECT_ID(N'dbo.JournalEntries') AND name = N'IX_JournalEntries_JournalVoucherId')
                BEGIN
                    CREATE INDEX IX_JournalEntries_JournalVoucherId ON dbo.JournalEntries(JournalVoucherId);
                END

                IF NOT EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE object_id = OBJECT_ID(N'dbo.JournalEntries') AND name = N'IX_JournalEntries_ChartOfAccountId')
                BEGIN
                    CREATE INDEX IX_JournalEntries_ChartOfAccountId ON dbo.JournalEntries(ChartOfAccountId);
                END

                IF NOT EXISTS (
                    SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_JournalEntries_JournalVouchers_JournalVoucherId')
                BEGIN
                    ALTER TABLE dbo.JournalEntries
                    ADD CONSTRAINT FK_JournalEntries_JournalVouchers_JournalVoucherId
                        FOREIGN KEY (JournalVoucherId) REFERENCES dbo.JournalVouchers(JournalVoucherId) ON DELETE NO ACTION;
                END

                IF NOT EXISTS (
                    SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_JournalEntries_ChartOfAccounts_ChartOfAccountId')
                BEGIN
                    ALTER TABLE dbo.JournalEntries
                    ADD CONSTRAINT FK_JournalEntries_ChartOfAccounts_ChartOfAccountId
                        FOREIGN KEY (ChartOfAccountId) REFERENCES dbo.ChartOfAccounts(ChartOfAccountId) ON DELETE NO ACTION;
                END
            END

            -- Seed the four accounts JournalPostingExecutorWorker's entry types map to.
            INSERT INTO dbo.ChartOfAccounts (ChartOfAccountId, Code, Name, AccountType, IsActive)
            SELECT NEWID(), v.Code, v.Name, v.AccountType, 1
            FROM (VALUES
                (N'1000-BANK', N'Bank / Cash Received', N'Asset'),
                (N'2000-CASHOUT', N'Cash-Out Clearing', N'Liability'),
                (N'5000-FEE', N'Processing Fee Expense', N'Expense'),
                (N'4000-FEEOFFSET', N'Fee Offset Revenue', N'Revenue')
            ) v(Code, Name, AccountType)
            WHERE NOT EXISTS (SELECT 1 FROM dbo.ChartOfAccounts a WHERE a.Code = v.Code);
            """;

        // Fixes a gap in the original four-account seed: JournalPostingExecutorWorker posts a
        // CreditCashIn entry for direct (non-card) CashIn transactions, but no account existed
        // for it — every CashIn transaction's journal posting threw KeyNotFoundException and
        // silently failed. Mirrors 2000-CASHOUT's role for the opposite cash direction.
        private static string BuildTenantChartOfAccountsCashInSeedSql() =>
            """
            IF OBJECT_ID(N'dbo.ChartOfAccounts', N'U') IS NOT NULL
            BEGIN
                INSERT INTO dbo.ChartOfAccounts (ChartOfAccountId, Code, Name, AccountType, IsActive)
                SELECT NEWID(), N'3000-CASHIN', N'Cash-In Clearing', N'Liability', 1
                WHERE NOT EXISTS (SELECT 1 FROM dbo.ChartOfAccounts a WHERE a.Code = N'3000-CASHIN');
            END
            """;

        // Adds the BatchNumber/TerminalId/MerchantId columns PosIdentifierExtractor populates at
        // import-commit time, plus ExtractionPatternsJson on the mapping template that drives it,
        // for Level7 (PosSettlementMatchWorker) POS-terminal batch settlement matching.
        private static string BuildTenantPosSettlementIdentifierFieldsSql() =>
            """
            IF OBJECT_ID(N'dbo.ImportedNormalizedRecords', N'U') IS NOT NULL
            BEGIN
                IF COL_LENGTH(N'dbo.ImportedNormalizedRecords', N'BatchNumber') IS NULL
                    ALTER TABLE dbo.ImportedNormalizedRecords ADD BatchNumber nvarchar(50) NULL;

                IF COL_LENGTH(N'dbo.ImportedNormalizedRecords', N'TerminalId') IS NULL
                    ALTER TABLE dbo.ImportedNormalizedRecords ADD TerminalId nvarchar(50) NULL;

                IF COL_LENGTH(N'dbo.ImportedNormalizedRecords', N'MerchantId') IS NULL
                    ALTER TABLE dbo.ImportedNormalizedRecords ADD MerchantId nvarchar(50) NULL;

                IF NOT EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE object_id = OBJECT_ID(N'dbo.ImportedNormalizedRecords') AND name = N'IX_ImportedNormalizedRecords_BatchNumber')
                    CREATE INDEX IX_ImportedNormalizedRecords_BatchNumber ON dbo.ImportedNormalizedRecords(BatchNumber);

                IF NOT EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE object_id = OBJECT_ID(N'dbo.ImportedNormalizedRecords') AND name = N'IX_ImportedNormalizedRecords_TerminalId_TransactionDate')
                    CREATE INDEX IX_ImportedNormalizedRecords_TerminalId_TransactionDate ON dbo.ImportedNormalizedRecords(TerminalId, TransactionDate);

                IF NOT EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE object_id = OBJECT_ID(N'dbo.ImportedNormalizedRecords') AND name = N'IX_ImportedNormalizedRecords_MerchantId_TransactionDate')
                    CREATE INDEX IX_ImportedNormalizedRecords_MerchantId_TransactionDate ON dbo.ImportedNormalizedRecords(MerchantId, TransactionDate);
            END

            IF OBJECT_ID(N'dbo.ImportMappingTemplates', N'U') IS NOT NULL
            BEGIN
                IF COL_LENGTH(N'dbo.ImportMappingTemplates', N'ExtractionPatternsJson') IS NULL
                    ALTER TABLE dbo.ImportMappingTemplates ADD ExtractionPatternsJson nvarchar(max) NULL;
            END
            """;

        // Per-tenant, admin-maintained non-business-day list used by BusinessDayCalculator for
        // Level7's T+N settlement date window.
        private static string BuildTenantBankingHolidaysSql() =>
            """
            IF OBJECT_ID(N'dbo.BankingHolidays', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.BankingHolidays (
                    BankingHolidayId uniqueidentifier NOT NULL PRIMARY KEY,
                    [Date] date NOT NULL,
                    Description nvarchar(200) NOT NULL,
                    CreatedAt datetime2 NOT NULL CONSTRAINT DF_BankingHolidays_CreatedAt DEFAULT SYSUTCDATETIME()
                );

                CREATE UNIQUE INDEX IX_BankingHolidays_Date ON dbo.BankingHolidays([Date]);
            END
            """;

        // T+N business-day settlement window, separate from the existing +/- DateToleranceDays
        // fuzzy-match window (that's symmetric same-day-ish tolerance; this is a directional
        // "the bank deposit lands N business days later" expectation).
        private static string BuildTenantReconciliationSettlementWindowSql() =>
            """
            IF OBJECT_ID(N'dbo.ReconciliationSettings', N'U') IS NOT NULL
            BEGIN
                IF COL_LENGTH(N'dbo.ReconciliationSettings', N'SettlementDateWindowDays') IS NULL
                    ALTER TABLE dbo.ReconciliationSettings ADD SettlementDateWindowDays int NOT NULL CONSTRAINT DF_ReconciliationSettings_SettlementDateWindowDays DEFAULT (3);
            END
            """;

        // Seeds the POS Clearing liability account Level7's split postings credit (Gross amount),
        // alongside the existing 1000-BANK (net) and 5000-FEE (MDR fee) accounts it reuses.
        private static string BuildTenantPosClearingAccountSql() =>
            """
            IF OBJECT_ID(N'dbo.ChartOfAccounts', N'U') IS NOT NULL
            BEGIN
                INSERT INTO dbo.ChartOfAccounts (ChartOfAccountId, Code, Name, AccountType, IsActive)
                SELECT NEWID(), N'6000-POSCLEARING', N'POS Clearing', N'Liability', 1
                WHERE NOT EXISTS (SELECT 1 FROM dbo.ChartOfAccounts a WHERE a.Code = N'6000-POSCLEARING');
            END
            """;



        // Adds the order/receipt reference field cashiers key in on manual entry, matching the
        // ReferenceNumber convention already used on ImportedNormalizedRecord so these
        // transactions can eventually be matched by the same key.
        private static string BuildTenantTransactionReferenceNumberSql() =>
            """
            IF OBJECT_ID(N'dbo.Transactions', N'U') IS NOT NULL
            BEGIN
                IF COL_LENGTH(N'dbo.Transactions', N'ReferenceNumber') IS NULL
                    ALTER TABLE dbo.Transactions ADD ReferenceNumber nvarchar(120) NULL;

                IF NOT EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE object_id = OBJECT_ID(N'dbo.Transactions') AND name = N'IX_Transactions_ReferenceNumber')
                BEGIN
                    CREATE INDEX IX_Transactions_ReferenceNumber ON dbo.Transactions (ReferenceNumber);
                END
            END
            """;

        // Another instance of the same gap class as CardLast4: tenant databases that had
        // dbo.ReconciliationEvents created by an earlier revision of
        // BuildTenantReconciliationJournalSchemaSql (before ReconciliationMatchGroupId,
        // MatchLevel and Details were added to that CREATE TABLE) never picked up the extra
        // columns, because MigrationReconciliationJournalSchema is tracked by name and only
        // runs once. That broke every worker that writes a ReconciliationEvent, e.g.
        // ErpGatewaySalesMatchWorker's Level3 run ("Invalid column name 'Details'/'MatchLevel'/
        // 'ReconciliationMatchGroupId'").
        private static string BuildTenantReconciliationEventsMatchGroupFieldsSql() =>
            """
            IF OBJECT_ID(N'dbo.ReconciliationEvents', N'U') IS NOT NULL
            BEGIN
                IF COL_LENGTH(N'dbo.ReconciliationEvents', N'ReconciliationMatchGroupId') IS NULL
                    ALTER TABLE dbo.ReconciliationEvents ADD ReconciliationMatchGroupId uniqueidentifier NULL;

                IF COL_LENGTH(N'dbo.ReconciliationEvents', N'MatchLevel') IS NULL
                    ALTER TABLE dbo.ReconciliationEvents ADD MatchLevel nvarchar(20) NOT NULL CONSTRAINT DF_ReconciliationEvents_MatchLevel DEFAULT (N'');

                IF COL_LENGTH(N'dbo.ReconciliationEvents', N'Details') IS NULL
                    ALTER TABLE dbo.ReconciliationEvents ADD Details nvarchar(2000) NULL;

                IF NOT EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE object_id = OBJECT_ID(N'dbo.ReconciliationEvents') AND name = N'IX_ReconciliationEvents_MatchLevel')
                BEGIN
                    CREATE INDEX IX_ReconciliationEvents_MatchLevel ON dbo.ReconciliationEvents(MatchLevel);
                END

                IF NOT EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE object_id = OBJECT_ID(N'dbo.ReconciliationEvents') AND name = N'IX_ReconciliationEvents_GroupId')
                BEGIN
                    CREATE INDEX IX_ReconciliationEvents_GroupId ON dbo.ReconciliationEvents(ReconciliationMatchGroupId);
                END

                IF OBJECT_ID(N'dbo.ReconciliationMatchGroups', N'U') IS NOT NULL AND NOT EXISTS (
                    SELECT 1 FROM sys.foreign_keys
                    WHERE name = N'FK_ReconciliationEvents_MatchGroups_GroupId')
                BEGIN
                    -- NO ACTION, not SET NULL: ImportBatches already cascades into
                    -- ReconciliationMatchGroups (ON DELETE SET NULL), which combined with a
                    -- second cascading path here makes SQL Server reject the constraint with
                    -- "may cause cycles or multiple cascade paths". Matches the NO ACTION used
                    -- by the other MatchGroups FKs (JournalEntries, JournalVouchers).
                    ALTER TABLE dbo.ReconciliationEvents
                    ADD CONSTRAINT FK_ReconciliationEvents_MatchGroups_GroupId
                        FOREIGN KEY (ReconciliationMatchGroupId)
                        REFERENCES dbo.ReconciliationMatchGroups(ReconciliationMatchGroupId)
                        ON DELETE NO ACTION;
                END
            END
            """;

        private static string BuildTenantReconciliationEventsImportBatchIdNullSql() =>
            """
            IF OBJECT_ID(N'dbo.ReconciliationEvents', N'U') IS NOT NULL
            BEGIN
                IF COL_LENGTH(N'dbo.ReconciliationEvents', N'ImportBatchId') IS NOT NULL
                BEGIN
                    ALTER TABLE dbo.ReconciliationEvents ALTER COLUMN ImportBatchId uniqueidentifier NULL;
                END
            END
            """;

        // WHY: dbo.ReconciliationEvents' CREATE TABLE above already declares ImportedNormalizedRecordId,
        // SourceType, Stage and Status as NULL, matching the nullable Guid?/string? properties on the
        // ReconciliationEvent model — MatchNotFound events legitimately have no imported record to
        // point at (e.g. OperationalMatchWorker, ErpGatewaySalesMatchWorker), and the model has always
        // allowed that. But that CREATE TABLE only runs for a tenant that doesn't have the table yet
        // (IF OBJECT_ID ... IS NULL) — a tenant database provisioned before these columns were made
        // nullable (found via a live DbUpdateException: one tenant's table still had
        // ImportedNormalizedRecordId/SourceType/Stage/Status as NOT NULL with Stage additionally
        // undersized at nvarchar(20) instead of nvarchar(50)) throws on every worker cycle that logs a
        // recordless MatchNotFound event. Same ALTER COLUMN retrofit pattern as
        // MigrationReconciliationEventsImportBatchIdNull above, for every column that migration didn't
        // cover — re-widening Stage back to its CREATE TABLE size at the same time.
        private static string BuildTenantReconciliationEventsRecordFieldsNullSql() =>
            """
            IF OBJECT_ID(N'dbo.ReconciliationEvents', N'U') IS NOT NULL
            BEGIN
                -- A single-column, non-unique index on the target column doesn't normally block
                -- ALTER COLUMN in SQL Server, but the legacy tenant this migration targets has one
                -- on each of these four columns and SQL Server refused the ALTER on at least one of
                -- them ("is dependent on column") — none of the four are part of the canonical
                -- CREATE TABLE index list above (only EventType/MatchLevel/CreatedAt/GroupId are), so
                -- dropping them here brings the table in line with current schema instead of
                -- reconstructing indexes that were never meant to exist.
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.ReconciliationEvents') AND name = N'IX_ReconciliationEvents_ImportedNormalizedRecordId')
                    DROP INDEX IX_ReconciliationEvents_ImportedNormalizedRecordId ON dbo.ReconciliationEvents;

                IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.ReconciliationEvents') AND name = N'IX_ReconciliationEvents_SourceType')
                    DROP INDEX IX_ReconciliationEvents_SourceType ON dbo.ReconciliationEvents;

                IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.ReconciliationEvents') AND name = N'IX_ReconciliationEvents_Stage')
                    DROP INDEX IX_ReconciliationEvents_Stage ON dbo.ReconciliationEvents;

                IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.ReconciliationEvents') AND name = N'IX_ReconciliationEvents_Status')
                    DROP INDEX IX_ReconciliationEvents_Status ON dbo.ReconciliationEvents;

                IF COL_LENGTH(N'dbo.ReconciliationEvents', N'ImportedNormalizedRecordId') IS NOT NULL
                BEGIN
                    ALTER TABLE dbo.ReconciliationEvents ALTER COLUMN ImportedNormalizedRecordId uniqueidentifier NULL;
                END

                IF COL_LENGTH(N'dbo.ReconciliationEvents', N'SourceType') IS NOT NULL
                BEGIN
                    ALTER TABLE dbo.ReconciliationEvents ALTER COLUMN SourceType nvarchar(100) NULL;
                END

                IF COL_LENGTH(N'dbo.ReconciliationEvents', N'Stage') IS NOT NULL
                BEGIN
                    ALTER TABLE dbo.ReconciliationEvents ALTER COLUMN Stage nvarchar(50) NULL;
                END

                IF COL_LENGTH(N'dbo.ReconciliationEvents', N'Status') IS NOT NULL
                BEGIN
                    ALTER TABLE dbo.ReconciliationEvents ALTER COLUMN Status nvarchar(30) NULL;
                END
            END
            """;

        // The frontend permission matrix has offered these source-type-scoped Imports grants
        // (and ImportsController/SourceTypeScope has been written to check them) since the
        // Imports scoped permissions section shipped, but no tenant schema ever actually seeded
        // the Permission rows themselves — the matrix's "Scoped Permissions" checkboxes for
        // Imports could never be saved (ADMIN.RolesController rejects unknown codes) and
        // ImportsController fell back to gating everything on TenantAdmin instead. This closes
        // that gap the same way MigrationTransactionCreatePermission did for ADMIN.TRANSACTIONS.CREATE.
        private static string BuildTenantImportsScopedPermissionsSql() =>
            """
            INSERT INTO dbo.Permissions (PermissionId, Code, Name, Description, Module)
            SELECT NEWID(), v.Code, v.Name, v.Description, v.Module
            FROM (VALUES
                (N'ADMIN.IMPORTS.POS.CREATE', N'POS Import Upload', N'Upload POS import files only', N'Imports'),
                (N'ADMIN.IMPORTS.POS.EDIT', N'POS Import Edit', N'Parse, map and validate POS import batches', N'Imports'),
                (N'ADMIN.IMPORTS.POS.COMMIT', N'POS Import Commit', N'Commit validated POS import batches', N'Imports'),
                (N'ADMIN.IMPORTS.POS_SETTLEMENT.CREATE', N'POS Settlement Import Upload', N'Upload POS terminal/acquirer settlement import files only', N'Imports'),
                (N'ADMIN.IMPORTS.POS_SETTLEMENT.EDIT', N'POS Settlement Import Edit', N'Parse, map and validate POS settlement import batches', N'Imports'),
                (N'ADMIN.IMPORTS.POS_SETTLEMENT.COMMIT', N'POS Settlement Import Commit', N'Commit validated POS settlement import batches', N'Imports'),
                (N'ADMIN.IMPORTS.ERP.CREATE', N'ERP Import Upload', N'Upload ERP import files only', N'Imports'),
                (N'ADMIN.IMPORTS.ERP.EDIT', N'ERP Import Edit', N'Parse, map and validate ERP import batches', N'Imports'),
                (N'ADMIN.IMPORTS.ERP.COMMIT', N'ERP Import Commit', N'Commit validated ERP import batches', N'Imports'),
                (N'ADMIN.IMPORTS.GATEWAY.CREATE', N'Gateway Import Upload', N'Upload Gateway import files only', N'Imports'),
                (N'ADMIN.IMPORTS.GATEWAY.EDIT', N'Gateway Import Edit', N'Parse, map and validate Gateway import batches', N'Imports'),
                (N'ADMIN.IMPORTS.GATEWAY.COMMIT', N'Gateway Import Commit', N'Commit validated Gateway import batches', N'Imports'),
                (N'ADMIN.IMPORTS.BANK.CREATE', N'Bank Import Upload', N'Upload Bank import files only', N'Imports'),
                (N'ADMIN.IMPORTS.BANK.EDIT', N'Bank Import Edit', N'Parse, map and validate Bank import batches', N'Imports'),
                (N'ADMIN.IMPORTS.BANK.COMMIT', N'Bank Import Commit', N'Commit validated Bank import batches', N'Imports')
            ) v(Code, Name, Description, Module)
            WHERE NOT EXISTS (SELECT 1 FROM dbo.Permissions p WHERE p.Code = v.Code);

            INSERT INTO dbo.RolePermissions (RoleId, PermissionId)
            SELECT r.RoleId, p.PermissionId
            FROM dbo.Roles r
            INNER JOIN dbo.Permissions p ON p.Code IN (
                N'ADMIN.IMPORTS.POS.CREATE', N'ADMIN.IMPORTS.POS.EDIT', N'ADMIN.IMPORTS.POS.COMMIT',
                N'ADMIN.IMPORTS.POS_SETTLEMENT.CREATE', N'ADMIN.IMPORTS.POS_SETTLEMENT.EDIT', N'ADMIN.IMPORTS.POS_SETTLEMENT.COMMIT',
                N'ADMIN.IMPORTS.ERP.CREATE', N'ADMIN.IMPORTS.ERP.EDIT', N'ADMIN.IMPORTS.ERP.COMMIT',
                N'ADMIN.IMPORTS.GATEWAY.CREATE', N'ADMIN.IMPORTS.GATEWAY.EDIT', N'ADMIN.IMPORTS.GATEWAY.COMMIT',
                N'ADMIN.IMPORTS.BANK.CREATE', N'ADMIN.IMPORTS.BANK.EDIT', N'ADMIN.IMPORTS.BANK.COMMIT'
            )
            WHERE r.Code = N'ADMIN'
              AND NOT EXISTS (
                  SELECT 1 FROM dbo.RolePermissions rp
                  WHERE rp.RoleId = r.RoleId AND rp.PermissionId = p.PermissionId
              );
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
