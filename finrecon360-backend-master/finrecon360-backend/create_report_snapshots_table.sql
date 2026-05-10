IF OBJECT_ID(N'[ReportSnapshots]', N'U') IS NULL
BEGIN
    CREATE TABLE [ReportSnapshots] (
        [ReportSnapshotId] uniqueidentifier NOT NULL,
        [SnapshotDate] date NOT NULL,
        [TotalUnmatchedCardCashouts] int NOT NULL,
        [PendingExceptions] int NOT NULL,
        [TotalJournalReady] int NOT NULL,
        [ReconciliationCompletionPercentage] decimal(5,2) NOT NULL,
        [TotalMatchGroupsConfirmed] int NOT NULL,
        [TotalFeeAdjustments] decimal(18,2) NOT NULL,
        [CreatedAt] datetime2 NOT NULL CONSTRAINT [DF_ReportSnapshots_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [PK_ReportSnapshots] PRIMARY KEY ([ReportSnapshotId])
    );
    CREATE UNIQUE INDEX [IX_ReportSnapshots_SnapshotDate] ON [ReportSnapshots] ([SnapshotDate]);
END
GO
