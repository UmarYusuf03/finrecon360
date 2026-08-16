-- =====================================================================
-- Repair Script: Add Missing Columns to ImportedNormalizedRecords Table
-- =====================================================================
-- This script adds missing columns that are required by the reconciliation workers
-- (BankStatementReconciliationWorker and JournalPostingExecutorWorker)
--
-- ERROR FIXED: Invalid column name 'ReferenceNumber'
--
-- Run this against each tenant database:
--   FinRecon360_Tenant_{tenantId}
--
-- To deploy against a specific failing tenant:
--   FinRecon360_Tenant_e4cb366a-30b6-4e39-b804-39280cde5648
-- =====================================================================

PRINT 'Starting repair of ImportedNormalizedRecords schema...';

-- Add ReferenceNumber column if it doesn't exist
IF COL_LENGTH('ImportedNormalizedRecords', 'ReferenceNumber') IS NULL
BEGIN
    ALTER TABLE [ImportedNormalizedRecords]
    ADD [ReferenceNumber] nvarchar(120) NULL;
    
    PRINT '[✓] Added ReferenceNumber column';
END
ELSE
BEGIN
    PRINT '[•] ReferenceNumber column already exists';
END

-- Add SettlementId column if it doesn't exist
IF COL_LENGTH('ImportedNormalizedRecords', 'SettlementId') IS NULL
BEGIN
    ALTER TABLE [ImportedNormalizedRecords]
    ADD [SettlementId] nvarchar(max) NULL;
    
    PRINT '[✓] Added SettlementId column';
END
ELSE
BEGIN
    PRINT '[•] SettlementId column already exists';
END

-- Create composite index on ReferenceNumber and TransactionDate if it doesn't exist
-- This index is used by BankStatementReconciliationWorker for settlement key lookups
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ImportedNormalizedRecords_ReferenceNumber_TransactionDate')
BEGIN
    CREATE INDEX [IX_ImportedNormalizedRecords_ReferenceNumber_TransactionDate] 
    ON [ImportedNormalizedRecords] ([ReferenceNumber], [TransactionDate]);
    
    PRINT '[✓] Created composite index on ReferenceNumber and TransactionDate';
END
ELSE
BEGIN
    PRINT '[•] Composite index already exists';
END

PRINT 'Column repair complete. Background services can now be restarted.';
