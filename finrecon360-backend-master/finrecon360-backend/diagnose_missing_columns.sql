-- =====================================================================
-- Diagnostic Script: Find Tenant Databases Missing Required Columns
-- =====================================================================
-- This script queries the FinRecon360 (control plane) database to:
-- 1. List all active tenants
-- 2. Identify which ones have missing ReferenceNumber column
-- 3. Generate repair commands for affected tenants
--
-- Run this on the control plane database (FinRecon360)
-- =====================================================================

USE [FinRecon360];

DECLARE @TenantId UNIQUEIDENTIFIER;
DECLARE @DatabaseName NVARCHAR(255);
DECLARE @ColumnExists INT;
DECLARE @SQL NVARCHAR(MAX);

-- Cursor to iterate through all active tenant databases
DECLARE tenant_cursor CURSOR FOR
SELECT 
    t.TenantId,
    td.EncryptedConnectionString
FROM Tenants t
INNER JOIN TenantDatabases td ON t.TenantId = td.TenantId
WHERE t.Status = 'Active'
    AND td.Status = 'Ready'
ORDER BY t.CreatedAt DESC;

PRINT '====================================================';
PRINT 'Scanning Tenant Databases for Missing Columns';
PRINT '====================================================';
PRINT '';

OPEN tenant_cursor;
FETCH NEXT FROM tenant_cursor INTO @TenantId, @DatabaseName;

-- Extract database name from encrypted connection string (rough pattern)
-- Note: This requires decryption in production - for now we'll use the naming pattern
SET @DatabaseName = 'FinRecon360_Tenant_' + CAST(@TenantId AS NVARCHAR(36));

WHILE @@FETCH_STATUS = 0
BEGIN
    -- Check if ImportedNormalizedRecords table exists and if ReferenceNumber column is missing
    SET @SQL = 'SELECT @Result = COUNT(*) FROM [' + @DatabaseName + '].INFORMATION_SCHEMA.COLUMNS 
                WHERE TABLE_NAME = ''ImportedNormalizedRecords'' AND COLUMN_NAME = ''ReferenceNumber''';
    
    EXEC sp_executesql @SQL, N'@Result INT OUTPUT', @ColumnExists OUTPUT;
    
    IF @ColumnExists = 0
    BEGIN
        PRINT 'AFFECTED: ' + @DatabaseName;
        PRINT '  Tenant ID: ' + CAST(@TenantId AS NVARCHAR(36));
        PRINT '  Status: MISSING ReferenceNumber column';
        PRINT '  Action: Execute repair_missing_import_columns.sql against this database';
        PRINT '';
    END
    
    FETCH NEXT FROM tenant_cursor INTO @TenantId, @DatabaseName;
END

CLOSE tenant_cursor;
DEALLOCATE tenant_cursor;

PRINT '====================================================';
PRINT 'Scan Complete';
PRINT '====================================================';
