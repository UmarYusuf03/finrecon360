Database Schema (SQL Server)

Tables

1) __EFMigrationsHistory
- Purpose: Tracks applied EF Core migrations.
- Columns:
  - MigrationId NVARCHAR(150) PK
  - ProductVersion NVARCHAR(32) NOT NULL

2) Users
- Purpose: Core user identity records with auth profile and status fields.
- Columns:
  - UserId UNIQUEIDENTIFIER PK
  - Email NVARCHAR(512) NOT NULL
  - DisplayName NVARCHAR(512) NULL
  - PhoneNumber NVARCHAR(64) NULL
  - EmailConfirmed BIT NOT NULL DEFAULT 0
  - IsActive BIT NOT NULL DEFAULT 1
  - Status NVARCHAR(32) NOT NULL DEFAULT 'Active'
  - IsSystemAdmin BIT NOT NULL DEFAULT 0
  - CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
  - UpdatedAt DATETIME2 NULL
  - FirstName NVARCHAR(512) NOT NULL
  - LastName NVARCHAR(512) NOT NULL
  - Country NVARCHAR(512) NOT NULL
  - Gender NVARCHAR(128) NOT NULL
  - PasswordHash NVARCHAR(1024) NOT NULL
  - VerificationCode NVARCHAR(128) NULL
  - VerificationCodeExpiresAt DATETIME2 NULL
  - ProfileImage VARBINARY(MAX) NULL
  - ProfileImageContentType NVARCHAR(200) NULL
- Indexes/Constraints:
  - PK_Users (UserId)
  - IX_Users_Email UNIQUE

3) Roles
- Purpose: Role definitions for RBAC.
- Columns:
  - RoleId UNIQUEIDENTIFIER PK
  - Code NVARCHAR(200) NOT NULL
  - Name NVARCHAR(200) NOT NULL
  - Description NVARCHAR(512) NULL
  - IsSystem BIT NOT NULL DEFAULT 0
  - IsActive BIT NOT NULL DEFAULT 1
  - CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
- Indexes/Constraints:
  - PK_Roles (RoleId)
  - IX_Roles_Code UNIQUE
  - IX_Roles_Name UNIQUE

4) Permissions
- Purpose: Permission definitions assignable to roles.
- Columns:
  - PermissionId UNIQUEIDENTIFIER PK
  - Code NVARCHAR(300) NOT NULL
  - Name NVARCHAR(400) NOT NULL
  - Description NVARCHAR(1000) NULL
  - Module NVARCHAR(200) NULL
  - CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
- Indexes/Constraints:
  - PK_Permissions (PermissionId)
  - IX_Permissions_Code UNIQUE

5) UserRoles
- Purpose: M:N user-role assignments.
- Columns:
  - UserId UNIQUEIDENTIFIER NOT NULL
  - RoleId UNIQUEIDENTIFIER NOT NULL
  - AssignedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
  - AssignedByUserId UNIQUEIDENTIFIER NULL
- Indexes/Constraints:
  - PK_UserRoles (UserId, RoleId)
  - IX_UserRoles_RoleId
  - FK_UserRoles_Users_UserId ON DELETE CASCADE
  - FK_UserRoles_Roles_RoleId ON DELETE CASCADE

6) RolePermissions
- Purpose: M:N role-permission grants.
- Columns:
  - RoleId UNIQUEIDENTIFIER NOT NULL
  - PermissionId UNIQUEIDENTIFIER NOT NULL
  - GrantedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
  - GrantedByUserId UNIQUEIDENTIFIER NULL
- Indexes/Constraints:
  - PK_RolePermissions (RoleId, PermissionId)
  - IX_RolePermissions_PermissionId
  - FK_RolePermissions_Roles_RoleId ON DELETE CASCADE
  - FK_RolePermissions_Permissions_PermissionId ON DELETE CASCADE

7) AuthActionTokens
- Purpose: Persisted auth action tokens for verification/reset flows.
- Columns:
  - AuthActionTokenId UNIQUEIDENTIFIER PK
  - UserId UNIQUEIDENTIFIER NULL
  - Email NVARCHAR(512) NOT NULL
  - Purpose NVARCHAR(100) NOT NULL
  - TokenHash VARBINARY(32) NOT NULL
  - TokenSalt VARBINARY(16) NOT NULL
  - ExpiresAt DATETIME2 NOT NULL
  - ConsumedAt DATETIME2 NULL
  - AttemptCount INT NOT NULL DEFAULT 0
  - CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
  - CreatedIp NVARCHAR(128) NULL
  - LastAttemptAt DATETIME2 NULL
- Indexes/Constraints:
  - PK_AuthActionTokens (AuthActionTokenId)
  - IX_AuthActionTokens_Email_Purpose_ExpiresAt
  - IX_AuthActionTokens_UserId
  - IX_AuthActionTokens_UserId_Purpose
  - FK_AuthActionTokens_Users_UserId ON DELETE SET NULL

8) AuditLogs
- Purpose: Audit trail for security-relevant actions.
- Columns:
  - AuditLogId UNIQUEIDENTIFIER PK
  - UserId UNIQUEIDENTIFIER NULL
  - Action NVARCHAR(400) NOT NULL
  - Entity NVARCHAR(400) NULL
  - EntityId NVARCHAR(200) NULL
  - Metadata NVARCHAR(MAX) NULL
  - CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
- Indexes/Constraints:
  - PK_AuditLogs (AuditLogId)
  - IX_AuditLogs_CreatedAt
  - IX_AuditLogs_UserId
  - FK_AuditLogs_Users_UserId ON DELETE SET NULL

9) AppComponents
- Purpose: Admin UI component registry for permission mapping.
- Columns:
  - AppComponentId UNIQUEIDENTIFIER PK
  - Code NVARCHAR(200) NOT NULL
  - Name NVARCHAR(400) NOT NULL
  - RoutePath NVARCHAR(400) NOT NULL
  - Category NVARCHAR(200) NULL
  - Description NVARCHAR(1000) NULL
  - IsActive BIT NOT NULL DEFAULT 1
  - CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
- Indexes/Constraints:
  - PK_AppComponents (AppComponentId)
  - IX_AppComponents_Code UNIQUE

10) PermissionActions
- Purpose: Admin action catalog for permission mapping.
- Columns:
  - PermissionActionId UNIQUEIDENTIFIER PK
  - Code NVARCHAR(100) NOT NULL
  - Name NVARCHAR(400) NOT NULL
  - Description NVARCHAR(1000) NULL
  - IsActive BIT NOT NULL DEFAULT 1
  - CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
- Indexes/Constraints:
  - PK_PermissionActions (PermissionActionId)
  - IX_PermissionActions_Code UNIQUE

11) TenantRegistrationRequests
- Purpose: Control-plane tenant onboarding requests pending admin review.
- Columns:
  - TenantRegistrationRequestId UNIQUEIDENTIFIER PK
  - BusinessName NVARCHAR(256) NOT NULL
  - AdminEmail NVARCHAR(256) NOT NULL
  - OnboardingMetadata NVARCHAR(MAX) NULL
  - Status NVARCHAR(32) NOT NULL
  - SubmittedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
  - ReviewedByUserId UNIQUEIDENTIFIER NULL
  - ReviewedAt DATETIME2 NULL
  - ReviewNote NVARCHAR(1000) NULL
- Indexes/Constraints:
  - PK_TenantRegistrationRequests (TenantRegistrationRequestId)
  - IX_TenantRegistrationRequests_AdminEmail
  - IX_TenantRegistrationRequests_Status

12) Tenants
- Purpose: Control-plane tenant record.
- Columns:
  - TenantId UNIQUEIDENTIFIER PK
  - Name NVARCHAR(256) NOT NULL
  - Status NVARCHAR(32) NOT NULL
  - CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
  - ActivatedAt DATETIME2 NULL
  - PrimaryDomain NVARCHAR(256) NULL
  - CurrentSubscriptionId UNIQUEIDENTIFIER NULL
- Indexes/Constraints:
  - PK_Tenants (TenantId)

13) TenantDatabases
- Purpose: Encrypted per-tenant DB connection storage.
- Columns:
  - TenantDatabaseId UNIQUEIDENTIFIER PK
  - TenantId UNIQUEIDENTIFIER NOT NULL
  - EncryptedConnectionString NVARCHAR(MAX) NOT NULL
  - Provider NVARCHAR(64) NOT NULL
  - CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
  - ProvisionedAt DATETIME2 NULL
  - Status NVARCHAR(32) NOT NULL
- Indexes/Constraints:
  - PK_TenantDatabases (TenantDatabaseId)
  - FK_TenantDatabases_Tenants_TenantId ON DELETE CASCADE

14) TenantUsers
- Purpose: Global user to tenant mapping.
- Columns:
  - TenantUserId UNIQUEIDENTIFIER PK
  - TenantId UNIQUEIDENTIFIER NOT NULL
  - UserId UNIQUEIDENTIFIER NOT NULL
  - Role NVARCHAR(32) NOT NULL
  - CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
- Indexes/Constraints:
  - PK_TenantUsers (TenantUserId)
  - IX_TenantUsers_TenantId_UserId UNIQUE
  - FK_TenantUsers_Tenants_TenantId ON DELETE CASCADE
  - FK_TenantUsers_Users_UserId ON DELETE CASCADE

15) Plans
- Purpose: Subscription plan catalog.
- Columns:
  - PlanId UNIQUEIDENTIFIER PK
  - Code NVARCHAR(64) NOT NULL
  - Name NVARCHAR(256) NOT NULL
  - PriceCents BIGINT NOT NULL
  - Currency NVARCHAR(8) NOT NULL
  - DurationDays INT NOT NULL
  - MaxAccounts INT NOT NULL
  - IsActive BIT NOT NULL DEFAULT 1
  - CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
- Indexes/Constraints:
  - PK_Plans (PlanId)
  - IX_Plans_Code UNIQUE

16) Subscriptions
- Purpose: Tenant subscription records.
- Columns:
  - SubscriptionId UNIQUEIDENTIFIER PK
  - TenantId UNIQUEIDENTIFIER NOT NULL
  - PlanId UNIQUEIDENTIFIER NOT NULL
  - Status NVARCHAR(32) NOT NULL
  - CurrentPeriodStart DATETIME2 NULL
  - CurrentPeriodEnd DATETIME2 NULL
  - CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
- Indexes/Constraints:
  - PK_Subscriptions (SubscriptionId)
  - FK_Subscriptions_Tenants_TenantId ON DELETE CASCADE
  - FK_Subscriptions_Plans_PlanId ON DELETE RESTRICT

17) PaymentSessions
- Purpose: Stripe checkout sessions for subscriptions.
- Columns:
  - PaymentSessionId UNIQUEIDENTIFIER PK
  - TenantId UNIQUEIDENTIFIER NOT NULL
  - SubscriptionId UNIQUEIDENTIFIER NOT NULL
  - StripeSessionId NVARCHAR(256) NOT NULL
  - StripeCustomerId NVARCHAR(256) NULL
  - Status NVARCHAR(32) NOT NULL
  - CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
  - PaidAt DATETIME2 NULL
- Indexes/Constraints:
  - PK_PaymentSessions (PaymentSessionId)
  - IX_PaymentSessions_StripeSessionId UNIQUE
  - FK_PaymentSessions_Tenants_TenantId ON DELETE CASCADE
  - FK_PaymentSessions_Subscriptions_SubscriptionId ON DELETE CASCADE

18) MagicLinkTokens
- Purpose: Control-plane onboarding magic link tokens.
- Columns:
  - MagicLinkTokenId UNIQUEIDENTIFIER PK
  - GlobalUserId UNIQUEIDENTIFIER NOT NULL
  - Purpose NVARCHAR(64) NOT NULL
  - TokenHash VARBINARY(32) NOT NULL
  - TokenSalt VARBINARY(16) NOT NULL
  - ExpiresAt DATETIME2 NOT NULL
  - UsedAt DATETIME2 NULL
  - CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
  - CreatedIp NVARCHAR(128) NULL
  - AttemptCount INT NOT NULL DEFAULT 0
  - LastAttemptAt DATETIME2 NULL
- Indexes/Constraints:
  - PK_MagicLinkTokens (MagicLinkTokenId)
  - IX_MagicLinkTokens_GlobalUserId_Purpose_ExpiresAt
  - FK_MagicLinkTokens_Users_GlobalUserId ON DELETE CASCADE

19) EnforcementActions
- Purpose: Audit trail for suspend/ban actions on tenants and users.
- Columns:
  - EnforcementActionId UNIQUEIDENTIFIER PK
  - TargetType NVARCHAR(16) NOT NULL
  - TargetId UNIQUEIDENTIFIER NOT NULL
  - ActionType NVARCHAR(16) NOT NULL
  - Reason NVARCHAR(1000) NOT NULL
  - CreatedBy UNIQUEIDENTIFIER NOT NULL
  - CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
  - ExpiresAt DATETIME2 NULL
- Indexes/Constraints:
  - PK_EnforcementActions (EnforcementActionId)
  - IX_EnforcementActions_TargetType_TargetId

Relationships Summary
- Users 1..* UserRoles (UserRoles.UserId -> Users.UserId, cascade)
- Roles 1..* UserRoles (UserRoles.RoleId -> Roles.RoleId, cascade)
- Roles 1..* RolePermissions (RolePermissions.RoleId -> Roles.RoleId, cascade)
- Permissions 1..* RolePermissions (RolePermissions.PermissionId -> Permissions.PermissionId, cascade)
- Users 1..* AuthActionTokens (AuthActionTokens.UserId -> Users.UserId, set null)
- Users 1..* AuditLogs (AuditLogs.UserId -> Users.UserId, set null)
- Tenants 1..* TenantUsers (TenantUsers.TenantId -> Tenants.TenantId, cascade)
- Tenants 1..* TenantDatabases (TenantDatabases.TenantId -> Tenants.TenantId, cascade)
- Tenants 1..* Subscriptions (Subscriptions.TenantId -> Tenants.TenantId, cascade)
- Plans 1..* Subscriptions (Subscriptions.PlanId -> Plans.PlanId, restrict)
- Subscriptions 1..* PaymentSessions (PaymentSessions.SubscriptionId -> Subscriptions.SubscriptionId, cascade)
