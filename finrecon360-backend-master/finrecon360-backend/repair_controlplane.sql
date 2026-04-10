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
