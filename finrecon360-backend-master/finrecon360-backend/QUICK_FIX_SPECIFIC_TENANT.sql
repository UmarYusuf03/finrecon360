-- =====================================================================
-- QUICK FIX: Repair Specific Tenant Database
-- =====================================================================
-- For the failing tenant: e4cb366a-30b6-4e39-b804-39280cde5648
-- 
-- INSTRUCTIONS:
-- 1. Open SQL Server Management Studio (SSMS)
-- 2. Connect to: localhost,1433 with sa / 19884@Zcc
-- 3. Execute the lines below in sequence
-- =====================================================================

-- Step 1: Switch to the specific tenant database
USE [FinRecon360_Tenant_e4cb366a-30b6-4e39-b804-39280cde5648];
GO

-- Step 2: Add missing ReferenceNumber column
IF COL_LENGTH('ImportedNormalizedRecords', 'ReferenceNumber') IS NULL
BEGIN
    ALTER TABLE [ImportedNormalizedRecords]
    ADD [ReferenceNumber] nvarchar(120) NULL;
    PRINT 'Column ReferenceNumber added successfully';
END
ELSE
    PRINT 'Column ReferenceNumber already exists';
GO

-- Step 3: Add missing SettlementId column  
IF COL_LENGTH('ImportedNormalizedRecords', 'SettlementId') IS NULL
BEGIN
    ALTER TABLE [ImportedNormalizedRecords]
    ADD [SettlementId] nvarchar(max) NULL;
    PRINT 'Column SettlementId added successfully';
END
ELSE
    PRINT 'Column SettlementId already exists';
GO

-- Step 4: Add index if not exists
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes 
    WHERE name = 'IX_ImportedNormalizedRecords_ReferenceNumber_TransactionDate'
)
BEGIN
    CREATE INDEX [IX_ImportedNormalizedRecords_ReferenceNumber_TransactionDate] 
    ON [ImportedNormalizedRecords] ([ReferenceNumber], [TransactionDate]);
    PRINT 'Index created successfully';
END
ELSE
    PRINT 'Index already exists';
GO

-- Step 5: Verify the columns exist
SELECT 
    COLUMN_NAME, 
    DATA_TYPE,
    IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS 
WHERE TABLE_NAME = 'ImportedNormalizedRecords' 
    AND COLUMN_NAME IN ('ReferenceNumber', 'SettlementId', 'TransactionDate')
ORDER BY ORDINAL_POSITION;
GO

PRINT '';
PRINT '====================================================';
PRINT 'Repair Complete!';
PRINT '====================================================';
PRINT '';
PRINT 'Next Steps:';
PRINT '1. Restart the BankReconciliationHostedService';
PRINT '2. Restart the JournalPostingHostedService';
PRINT '3. Monitor logs for errors';
PRINT '';
