using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace finrecon360_backend.Migrations
{
    [Migration("20260302120000_AddControlPlaneTablesManual")]
    public partial class AddControlPlaneTablesManual : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'[Tenants]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [Tenants] (
                        [TenantId] uniqueidentifier NOT NULL,
                        [Name] nvarchar(256) NOT NULL,
                        [Status] nvarchar(32) NOT NULL CONSTRAINT [DF_Tenants_Status] DEFAULT (N'Pending'),
                        [CreatedAt] datetime2 NOT NULL CONSTRAINT [DF_Tenants_CreatedAt] DEFAULT (SYSUTCDATETIME()),
                        [ActivatedAt] datetime2 NULL,
                        [PrimaryDomain] nvarchar(256) NULL,
                        [CurrentSubscriptionId] uniqueidentifier NULL,
                        CONSTRAINT [PK_Tenants] PRIMARY KEY ([TenantId])
                    );
                END
                """
            );

            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'[TenantRegistrationRequests]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [TenantRegistrationRequests] (
                        [TenantRegistrationRequestId] uniqueidentifier NOT NULL,
                        [BusinessName] nvarchar(256) NOT NULL,
                        [AdminEmail] nvarchar(256) NOT NULL,
                        [OnboardingMetadata] nvarchar(max) NULL,
                        [Status] nvarchar(32) NOT NULL CONSTRAINT [DF_TenantRegistrationRequests_Status] DEFAULT (N'PENDING_REVIEW'),
                        [SubmittedAt] datetime2 NOT NULL CONSTRAINT [DF_TenantRegistrationRequests_SubmittedAt] DEFAULT (SYSUTCDATETIME()),
                        [ReviewedByUserId] uniqueidentifier NULL,
                        [ReviewedAt] datetime2 NULL,
                        [ReviewNote] nvarchar(1000) NULL,
                        CONSTRAINT [PK_TenantRegistrationRequests] PRIMARY KEY ([TenantRegistrationRequestId]),
                        CONSTRAINT [FK_TenantRegistrationRequests_Users_ReviewedByUserId] FOREIGN KEY ([ReviewedByUserId]) REFERENCES [Users] ([UserId]) ON DELETE SET NULL
                    );
                    CREATE INDEX [IX_TenantRegistrationRequests_AdminEmail] ON [TenantRegistrationRequests] ([AdminEmail]);
                    CREATE INDEX [IX_TenantRegistrationRequests_Status] ON [TenantRegistrationRequests] ([Status]);
                END
                """
            );

            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'[TenantDatabases]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [TenantDatabases] (
                        [TenantDatabaseId] uniqueidentifier NOT NULL,
                        [TenantId] uniqueidentifier NOT NULL,
                        [EncryptedConnectionString] nvarchar(max) NOT NULL,
                        [Provider] nvarchar(64) NOT NULL CONSTRAINT [DF_TenantDatabases_Provider] DEFAULT (N'SqlServer'),
                        [Status] nvarchar(32) NOT NULL CONSTRAINT [DF_TenantDatabases_Status] DEFAULT (N'Provisioning'),
                        [CreatedAt] datetime2 NOT NULL CONSTRAINT [DF_TenantDatabases_CreatedAt] DEFAULT (SYSUTCDATETIME()),
                        [ProvisionedAt] datetime2 NULL,
                        CONSTRAINT [PK_TenantDatabases] PRIMARY KEY ([TenantDatabaseId]),
                        CONSTRAINT [FK_TenantDatabases_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([TenantId]) ON DELETE CASCADE
                    );
                END
                """
            );

            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'[TenantUsers]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [TenantUsers] (
                        [TenantUserId] uniqueidentifier NOT NULL,
                        [TenantId] uniqueidentifier NOT NULL,
                        [UserId] uniqueidentifier NOT NULL,
                        [Role] nvarchar(32) NOT NULL CONSTRAINT [DF_TenantUsers_Role] DEFAULT (N'TenantUser'),
                        [CreatedAt] datetime2 NOT NULL CONSTRAINT [DF_TenantUsers_CreatedAt] DEFAULT (SYSUTCDATETIME()),
                        CONSTRAINT [PK_TenantUsers] PRIMARY KEY ([TenantUserId]),
                        CONSTRAINT [FK_TenantUsers_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([TenantId]) ON DELETE CASCADE,
                        CONSTRAINT [FK_TenantUsers_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([UserId]) ON DELETE CASCADE
                    );
                    CREATE UNIQUE INDEX [IX_TenantUsers_TenantId_UserId] ON [TenantUsers] ([TenantId], [UserId]);
                END
                """
            );

            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'[Plans]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [Plans] (
                        [PlanId] uniqueidentifier NOT NULL,
                        [Code] nvarchar(64) NOT NULL,
                        [Name] nvarchar(256) NOT NULL,
                        [PriceCents] bigint NOT NULL,
                        [Currency] nvarchar(8) NOT NULL CONSTRAINT [DF_Plans_Currency] DEFAULT (N'USD'),
                        [DurationDays] int NOT NULL,
                        [MaxAccounts] int NOT NULL,
                        [IsActive] bit NOT NULL CONSTRAINT [DF_Plans_IsActive] DEFAULT (1),
                        [CreatedAt] datetime2 NOT NULL CONSTRAINT [DF_Plans_CreatedAt] DEFAULT (SYSUTCDATETIME()),
                        CONSTRAINT [PK_Plans] PRIMARY KEY ([PlanId])
                    );
                    CREATE UNIQUE INDEX [IX_Plans_Code] ON [Plans] ([Code]);
                END
                """
            );

            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'[Subscriptions]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [Subscriptions] (
                        [SubscriptionId] uniqueidentifier NOT NULL,
                        [TenantId] uniqueidentifier NOT NULL,
                        [PlanId] uniqueidentifier NOT NULL,
                        [Status] nvarchar(32) NOT NULL CONSTRAINT [DF_Subscriptions_Status] DEFAULT (N'PendingPayment'),
                        [CurrentPeriodStart] datetime2 NULL,
                        [CurrentPeriodEnd] datetime2 NULL,
                        [CreatedAt] datetime2 NOT NULL CONSTRAINT [DF_Subscriptions_CreatedAt] DEFAULT (SYSUTCDATETIME()),
                        CONSTRAINT [PK_Subscriptions] PRIMARY KEY ([SubscriptionId]),
                        CONSTRAINT [FK_Subscriptions_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([TenantId]) ON DELETE CASCADE,
                        CONSTRAINT [FK_Subscriptions_Plans_PlanId] FOREIGN KEY ([PlanId]) REFERENCES [Plans] ([PlanId]) ON DELETE NO ACTION
                    );
                END
                """
            );

            migrationBuilder.Sql(
                """
                IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Tenants_Subscriptions_CurrentSubscriptionId')
                BEGIN
                    ALTER TABLE [Tenants]
                    ADD CONSTRAINT [FK_Tenants_Subscriptions_CurrentSubscriptionId]
                    FOREIGN KEY ([CurrentSubscriptionId]) REFERENCES [Subscriptions] ([SubscriptionId]) ON DELETE SET NULL;
                END
                """
            );

            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'[PaymentSessions]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [PaymentSessions] (
                        [PaymentSessionId] uniqueidentifier NOT NULL,
                        [TenantId] uniqueidentifier NOT NULL,
                        [SubscriptionId] uniqueidentifier NOT NULL,
                        [StripeSessionId] nvarchar(256) NOT NULL,
                        [StripeCustomerId] nvarchar(256) NULL,
                        [Status] nvarchar(32) NOT NULL CONSTRAINT [DF_PaymentSessions_Status] DEFAULT (N'Created'),
                        [CreatedAt] datetime2 NOT NULL CONSTRAINT [DF_PaymentSessions_CreatedAt] DEFAULT (SYSUTCDATETIME()),
                        [PaidAt] datetime2 NULL,
                        CONSTRAINT [PK_PaymentSessions] PRIMARY KEY ([PaymentSessionId]),
                        CONSTRAINT [FK_PaymentSessions_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([TenantId]) ON DELETE CASCADE,
                        CONSTRAINT [FK_PaymentSessions_Subscriptions_SubscriptionId] FOREIGN KEY ([SubscriptionId]) REFERENCES [Subscriptions] ([SubscriptionId]) ON DELETE CASCADE
                    );
                    CREATE UNIQUE INDEX [IX_PaymentSessions_StripeSessionId] ON [PaymentSessions] ([StripeSessionId]);
                END
                """
            );

            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'[MagicLinkTokens]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [MagicLinkTokens] (
                        [MagicLinkTokenId] uniqueidentifier NOT NULL,
                        [GlobalUserId] uniqueidentifier NOT NULL,
                        [Purpose] nvarchar(64) NOT NULL,
                        [TokenHash] varbinary(32) NOT NULL,
                        [TokenSalt] varbinary(16) NOT NULL,
                        [ExpiresAt] datetime2 NOT NULL,
                        [UsedAt] datetime2 NULL,
                        [CreatedAt] datetime2 NOT NULL CONSTRAINT [DF_MagicLinkTokens_CreatedAt] DEFAULT (SYSUTCDATETIME()),
                        [CreatedIp] nvarchar(128) NULL,
                        [LastAttemptAt] datetime2 NULL,
                        [AttemptCount] int NOT NULL CONSTRAINT [DF_MagicLinkTokens_AttemptCount] DEFAULT (0),
                        CONSTRAINT [PK_MagicLinkTokens] PRIMARY KEY ([MagicLinkTokenId]),
                        CONSTRAINT [FK_MagicLinkTokens_Users_GlobalUserId] FOREIGN KEY ([GlobalUserId]) REFERENCES [Users] ([UserId]) ON DELETE CASCADE
                    );
                    CREATE INDEX [IX_MagicLinkTokens_GlobalUserId_Purpose_ExpiresAt] ON [MagicLinkTokens] ([GlobalUserId], [Purpose], [ExpiresAt]);
                END
                """
            );

            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'[EnforcementActions]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [EnforcementActions] (
                        [EnforcementActionId] uniqueidentifier NOT NULL,
                        [TargetType] nvarchar(16) NOT NULL,
                        [TargetId] uniqueidentifier NOT NULL,
                        [ActionType] nvarchar(16) NOT NULL,
                        [Reason] nvarchar(1000) NOT NULL,
                        [CreatedBy] uniqueidentifier NOT NULL,
                        [CreatedAt] datetime2 NOT NULL CONSTRAINT [DF_EnforcementActions_CreatedAt] DEFAULT (SYSUTCDATETIME()),
                        [ExpiresAt] datetime2 NULL,
                        CONSTRAINT [PK_EnforcementActions] PRIMARY KEY ([EnforcementActionId])
                    );
                    CREATE INDEX [IX_EnforcementActions_TargetType_TargetId] ON [EnforcementActions] ([TargetType], [TargetId]);
                END
                """
            );

            migrationBuilder.Sql(
                """
                IF COL_LENGTH('Users', 'Status') IS NULL
                BEGIN
                    ALTER TABLE [Users] ADD [Status] nvarchar(32) NOT NULL CONSTRAINT [DF_Users_Status] DEFAULT (N'Active');
                END

                IF COL_LENGTH('Users', 'IsSystemAdmin') IS NULL
                BEGIN
                    ALTER TABLE [Users] ADD [IsSystemAdmin] bit NOT NULL CONSTRAINT [DF_Users_IsSystemAdmin] DEFAULT (0);
                END
                """
            );
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF OBJECT_ID(N'[EnforcementActions]', N'U') IS NOT NULL DROP TABLE [EnforcementActions];");
            migrationBuilder.Sql("IF OBJECT_ID(N'[MagicLinkTokens]', N'U') IS NOT NULL DROP TABLE [MagicLinkTokens];");
            migrationBuilder.Sql("IF OBJECT_ID(N'[PaymentSessions]', N'U') IS NOT NULL DROP TABLE [PaymentSessions];");
            migrationBuilder.Sql("IF OBJECT_ID(N'[TenantUsers]', N'U') IS NOT NULL DROP TABLE [TenantUsers];");
            migrationBuilder.Sql("IF OBJECT_ID(N'[TenantDatabases]', N'U') IS NOT NULL DROP TABLE [TenantDatabases];");
            migrationBuilder.Sql("IF OBJECT_ID(N'[TenantRegistrationRequests]', N'U') IS NOT NULL DROP TABLE [TenantRegistrationRequests];");
            migrationBuilder.Sql("IF OBJECT_ID(N'[Subscriptions]', N'U') IS NOT NULL DROP TABLE [Subscriptions];");
            migrationBuilder.Sql("IF OBJECT_ID(N'[Plans]', N'U') IS NOT NULL DROP TABLE [Plans];");
            migrationBuilder.Sql("IF OBJECT_ID(N'[Tenants]', N'U') IS NOT NULL DROP TABLE [Tenants];");
        }
    }
}
