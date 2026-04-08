using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using finrecon360_backend.Data;

#nullable disable

namespace finrecon360_backend.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260302123000_ReconcileControlPlaneSchema")]
    public partial class ReconcileControlPlaneSchema : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'[PaymentSessions]', N'U') IS NOT NULL
                    DROP TABLE [PaymentSessions];

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
                    CONSTRAINT [FK_PaymentSessions_Subscriptions_SubscriptionId] FOREIGN KEY ([SubscriptionId]) REFERENCES [Subscriptions] ([SubscriptionId]) ON DELETE NO ACTION
                );

                CREATE UNIQUE INDEX [IX_PaymentSessions_StripeSessionId] ON [PaymentSessions]([StripeSessionId]);
                CREATE INDEX [IX_PaymentSessions_SubscriptionId] ON [PaymentSessions]([SubscriptionId]);
                CREATE INDEX [IX_PaymentSessions_TenantId] ON [PaymentSessions]([TenantId]);
                """
            );

            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'[MagicLinkTokens]', N'U') IS NOT NULL
                    DROP TABLE [MagicLinkTokens];

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
                    [AttemptCount] int NOT NULL CONSTRAINT [DF_MagicLinkTokens_AttemptCount] DEFAULT (0),
                    [LastAttemptAt] datetime2 NULL,
                    CONSTRAINT [PK_MagicLinkTokens] PRIMARY KEY ([MagicLinkTokenId]),
                    CONSTRAINT [FK_MagicLinkTokens_Users_GlobalUserId] FOREIGN KEY ([GlobalUserId]) REFERENCES [Users]([UserId]) ON DELETE CASCADE
                );

                CREATE INDEX [IX_MagicLinkTokens_GlobalUserId_Purpose_ExpiresAt]
                    ON [MagicLinkTokens]([GlobalUserId], [Purpose], [ExpiresAt]);
                """
            );

            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'[EnforcementActions]', N'U') IS NOT NULL
                    DROP TABLE [EnforcementActions];

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

                CREATE INDEX [IX_EnforcementActions_TargetType_TargetId]
                    ON [EnforcementActions]([TargetType], [TargetId]);
                """
            );
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty. This reconciliation migration is forward-only.
        }
    }
}
