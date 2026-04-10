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
